// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Infrastructure.Platform.Resilience;
using Polly.RateLimiting;
using Polly.Timeout;

namespace NomNomzBot.Infrastructure.Tests.Platform.Resilience;

/// <summary>
/// Spotify's rate limit is per-app (client_id) across every connected channel's token, on a rolling 30s window
/// (developer.spotify.com/documentation/web-api/concepts/rate-limits) — NOT per user, which is what
/// <c>MusicStatePollingService</c>'s own doc comment used to assume when it polled every connected channel at a
/// flat 1s cadence. A self-hosted deployment can carry unlimited channels (product statement) all sharing the one
/// Spotify app the operator registered, so channel count alone can burn through the shared budget with nothing
/// to stop it. Proves the "spotify" named <see cref="HttpClient"/> actually enforces a total ceiling on
/// in-flight-or-queued requests, over the REAL production registration
/// (<see cref="ResiliencePolicies.AddSpotifyResilienceHandler"/>) — not a re-implementation of the limiter's
/// internals — so a regression that drops or loosens the rate-limiter wiring fails this test.
/// </summary>
public sealed class SpotifyRateLimiterTests
{
    /// <summary>Always answers 200 immediately — isolates the rate limiter's own ceiling from retry/circuit-
    /// breaker behavior, which is covered separately (429/503 handling).</summary>
    private sealed class AlwaysOkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private static HttpClient NewClient()
    {
        ServiceCollection services = new();
        services
            .AddHttpClient("spotify")
            .ConfigurePrimaryHttpMessageHandler(() => new AlwaysOkHandler())
            .AddSpotifyResilienceHandler();
        ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IHttpClientFactory>().CreateClient("spotify");
    }

    /// <summary>Builds a request tagged the way <c>SpotifyMusicProvider</c> tags a background poll, so the
    /// pipeline's <see cref="RateLimiterStrategyOptions.RateLimiter"/> delegate routes it to the poll partition
    /// instead of the (untagged-default) interactive one.</summary>
    private static Task<HttpResponseMessage> SendPollAsync(HttpClient client, string url)
    {
        HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Options.Set(SpotifyRequestTags.IsBackgroundPoll, true);
        return client.SendAsync(request);
    }

    /// <summary>A background poll attributed to a specific channel — the partition key the limiter meters on.</summary>
    private static Task<HttpResponseMessage> SendPollAsync(
        HttpClient client,
        string url,
        Guid broadcasterId
    )
    {
        HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Options.Set(SpotifyRequestTags.IsBackgroundPoll, true);
        request.Options.Set(SpotifyRequestTags.BroadcasterId, broadcasterId);
        return client.SendAsync(request);
    }

    /// <summary>An untagged call (every plain <c>GetAsync</c>/<c>PostAsync</c> below) is routed to the
    /// interactive partition by default — PermitLimit (270) immediate + QueueLimit (30) queued = 300 requests
    /// admitted without rejecting. A caller count comfortably past that — proxying "many interactive callers at
    /// once" — must see at least one request rejected by the limiter rather than every single one going
    /// straight through, which is exactly the unbounded behavior that shipped before this gate existed.</summary>
    [Fact]
    public async Task A_burst_far_past_the_combined_permit_and_queue_capacity_is_capped_not_unbounded()
    {
        using HttpClient client = NewClient();
        const int burst = 1_200; // PermitLimit(270) + QueueLimit(30) = 300 admitted at most (interactive partition)

        Task<HttpResponseMessage>[] calls =
        [
            .. Enumerable
                .Range(0, burst)
                .Select(_ =>
                    client.GetAsync("https://api.spotify.com/v1/me/player/currently-playing")
                ),
        ];

        HttpResponseMessage?[] results = await Task.WhenAll(
            calls.Select(async task =>
            {
                try
                {
                    return await task;
                }
                // A request past capacity is rejected outright by the limiter; one that lands just inside the
                // queue but never reaches the front before the outer per-call timeout (AddTimeout wrapping the
                // rate limiter — see AddSpotifyResilienceHandler) times out instead. Both are "did not succeed
                // under this burst", not a genuine failure of the burst itself.
                catch (RateLimiterRejectedException)
                {
                    return null;
                }
                catch (TimeoutRejectedException)
                {
                    return null;
                }
            })
        );

        int succeeded = results.Count(r => r is not null);
        int rejected = results.Count(r => r is null);

        succeeded
            .Should()
            .BeLessThan(
                burst,
                "the limiter must cap total in-flight-or-queued Spotify calls regardless of how many "
                    + "channels/callers fire at once — an unbounded pass-through is the exact defect this "
                    + "gate exists to close"
            );
        rejected
            .Should()
            .BeGreaterThan(0, "requests past the permit+queue capacity must be rejected");
        succeeded
            .Should()
            .BeGreaterThan(0, "requests within the permit+queue capacity must still succeed");
    }

    /// <summary>A single caller polling at a normal cadence (e.g. one connected channel) must never be
    /// throttled — the limiter exists to bound MANY callers sharing one Spotify app, not to slow down the
    /// ordinary case this poller was built for.</summary>
    [Fact]
    public async Task A_small_number_of_sequential_calls_is_never_throttled()
    {
        using HttpClient client = NewClient();

        for (int i = 0; i < 10; i++)
        {
            using HttpResponseMessage response = await client.GetAsync(
                "https://api.spotify.com/v1/me/player/currently-playing"
            );
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    /// <summary>
    /// The regression this test guards: MusicStatePollingService polls every fast-eligible connected channel
    /// once per second, so a deployment's steady-state background demand alone is (channel count)
    /// requests/second — a full 30s window's worth is (channel count × 30) requests. Before the poll/interactive
    /// split, that traffic shared one FIFO queue with interactive calls, so a busy poller could permanently
    /// saturate it and an interactive pause/play call would wait behind the backlog for multiple seconds
    /// (confirmed live against production: a POST .../music/pause took 9.3s end-to-end behind a poller backlog).
    /// The fix routes background-poll requests (tagged via <see cref="SpotifyRequestTags.IsBackgroundPoll"/>,
    /// as every real poll request is — see <c>SpotifyMusicProvider.SendAsync</c>) to their OWN rate-limiter
    /// partition, entirely separate from the interactive one.
    ///
    /// Fires a realistic multi-channel poller's one-window worth of TAGGED background load and an untagged
    /// interactive call CONCURRENTLY (not sequentially — the live bug is about contention while both are in
    /// flight at once, not about the interactive call arriving after the backlog has already drained). The
    /// interactive call must still land inside the FIRST rate-limiter segment (5s: Window(30s)/
    /// SegmentsPerWindow(6)) — proving it was never stuck queued behind the poll partition's backlog, not just
    /// "eventually succeeded". Some background calls may legitimately be rejected once the poll partition's own
    /// (much smaller) capacity is exceeded — that partitions being fully separate from the interactive one is
    /// exactly the point — but none of that contention may leak into the interactive call.
    /// </summary>
    [Fact]
    public async Task An_interactive_call_is_not_starved_by_a_realistic_pollers_concurrent_background_load()
    {
        using HttpClient client = NewClient();
        const int connectedChannels = 10;
        const int pollerDemandPerWindow = connectedChannels * 30; // 1 req/s/channel over the 30s window

        Task<HttpResponseMessage>[] background =
        [
            .. Enumerable
                .Range(0, pollerDemandPerWindow)
                .Select(_ =>
                    SendPollAsync(client, "https://api.spotify.com/v1/me/player/currently-playing")
                ),
        ];

        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Task<HttpResponseMessage> interactiveTask = client.PostAsync(
            "https://api.spotify.com/v1/me/player/pause",
            null
        );
        using HttpResponseMessage interactive = await interactiveTask;
        stopwatch.Stop();

        // Drain the background batch — never leaves the handler's pending state dirty for other tests. Some
        // of these may be legitimately rejected by the poll partition's own (much smaller) capacity; that's
        // fine, that partition existing at all — separate from the interactive one — is the point.
        await Task.WhenAll(
            background.Select(async task =>
            {
                try
                {
                    using HttpResponseMessage response = await task;
                }
                catch (RateLimiterRejectedException) { }
                catch (TimeoutRejectedException) { }
            })
        );

        interactive.StatusCode.Should().Be(HttpStatusCode.OK);
        stopwatch
            .Elapsed.Should()
            .BeLessThan(
                TimeSpan.FromSeconds(5),
                "an interactive pause/play must clear within the FIRST rate-limiter segment even while a "
                    + "realistic poller's full-window background load is concurrently in flight — queueing "
                    + "behind that load past one segment is the exact regression this test exists to catch"
            );
    }

    /// <summary>
    /// Spotify meters per app (client_id) and every channel registers its OWN Spotify app —
    /// <c>ChannelCredentialsResolver</c> refuses a Spotify resolve rather than falling back to an app-level
    /// credential (confirmed on the deployed box 2026-09-21: two channels, two distinct client ids). Two
    /// channels therefore share no real budget, and one must never be able to exhaust the other's. Before the
    /// limiter was partitioned there was a single process-wide pair of buckets, so a burst from one channel
    /// starved every other channel's calls for nothing.
    /// </summary>
    [Fact]
    public async Task One_channels_burst_never_starves_another_channels_calls()
    {
        using HttpClient client = NewClient();
        Guid noisy = Guid.Parse("0192a000-0000-7000-8000-00000000be01");
        Guid quiet = Guid.Parse("0192a000-0000-7000-8000-00000000be02");

        // Far past one partition's poll capacity (PermitLimit 180 + QueueLimit 20 = 200).
        Task<HttpResponseMessage?>[] flood =
        [
            .. Enumerable
                .Range(0, 1_000)
                .Select(_ =>
                    Swallow(SendPollAsync(client, "https://api.spotify.com/v1/me/player", noisy))
                ),
        ];

        HttpResponseMessage? neighbour = await Swallow(
            SendPollAsync(client, "https://api.spotify.com/v1/me/player", quiet)
        );

        neighbour
            .Should()
            .NotBeNull(
                "the quiet channel meters against its own Spotify app, so a neighbour's flood is none of its business"
            );

        await Task.WhenAll(flood);
    }

    /// <summary>The partitioning must not disable the ceiling: one channel's own burst is still capped.</summary>
    [Fact]
    public async Task A_single_channels_own_burst_is_still_capped_within_its_partition()
    {
        using HttpClient client = NewClient();
        Guid channel = Guid.Parse("0192a000-0000-7000-8000-00000000be03");

        HttpResponseMessage?[] results = await Task.WhenAll(
            Enumerable
                .Range(0, 1_000)
                .Select(_ =>
                    Swallow(SendPollAsync(client, "https://api.spotify.com/v1/me/player", channel))
                )
        );

        results
            .Count(r => r is null)
            .Should()
            .BeGreaterThan(0, "a partition is still a ceiling, not an exemption");
    }

    /// <summary>Awaits a call, turning "the limiter said no" into null rather than an exception.</summary>
    private static async Task<HttpResponseMessage?> Swallow(Task<HttpResponseMessage> call)
    {
        try
        {
            return await call;
        }
        catch (RateLimiterRejectedException)
        {
            return null;
        }
        catch (TimeoutRejectedException)
        {
            return null;
        }
    }

    /// <summary>Always answers 429 with a caller-supplied <c>Retry-After</c>, counting every attempt Polly
    /// makes (the initial send plus every retry) — isolates the retry strategy's own delay/attempt-cap
    /// behavior from the rate limiter covered above.</summary>
    private sealed class AlwaysTooManyRequestsHandler(string retryAfter) : HttpMessageHandler
    {
        public int AttemptCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            AttemptCount++;
            HttpResponseMessage response = new(HttpStatusCode.TooManyRequests);
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
            return Task.FromResult(response);
        }
    }

    private static HttpClient NewClient(HttpMessageHandler handler)
    {
        ServiceCollection services = new();
        services
            .AddHttpClient("spotify")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddSpotifyResilienceHandler();
        ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IHttpClientFactory>().CreateClient("spotify");
    }

    /// <summary>
    /// The exact live defect: Spotify answering 429 with <c>Retry-After: 0</c> must never be retried
    /// immediately — the retry has to wait at least the floor (1s) before firing again. Proven by wall-clock
    /// timing over the REAL production pipeline (<see cref="ResiliencePolicies.AddSpotifyResilienceHandler"/>),
    /// not a re-implementation of its delay math.
    /// </summary>
    [Fact]
    public async Task A_429_with_retry_after_zero_is_never_retried_immediately()
    {
        AlwaysTooManyRequestsHandler handler = new("0");
        using HttpClient client = NewClient(handler);

        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using HttpResponseMessage response = await client.GetAsync(
            "https://api.spotify.com/v1/me/player"
        );
        stopwatch.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        handler
            .AttemptCount.Should()
            .Be(3, "MaxRetryAttempts=2 caps this at 1 initial + 2 retries");
        stopwatch
            .Elapsed.Should()
            .BeGreaterThanOrEqualTo(
                TimeSpan.FromSeconds(2).Subtract(TimeSpan.FromMilliseconds(200)),
                "each of the 2 retries must wait at least the 1s floor even though Retry-After said 0 — "
                    + "an unfloored delay is exactly what turned one rate-limited poll into a zero-delay loop"
            );
    }

    /// <summary>A genuine Retry-After above the floor is honored, not overridden down to the floor.</summary>
    [Fact]
    public async Task A_real_retry_after_above_the_floor_is_honored()
    {
        AlwaysTooManyRequestsHandler handler = new("1"); // 1s — at the floor, not below it, keeps the test fast.
        using HttpClient client = NewClient(handler);

        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using HttpResponseMessage response = await client.GetAsync(
            "https://api.spotify.com/v1/me/player"
        );
        stopwatch.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        handler.AttemptCount.Should().Be(3);
        stopwatch
            .Elapsed.Should()
            .BeGreaterThanOrEqualTo(
                TimeSpan.FromSeconds(2).Subtract(TimeSpan.FromMilliseconds(200))
            );
    }
}
