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

    /// <summary>PermitLimit (60) immediate + QueueLimit (50) queued = 110 requests the limiter admits without
    /// rejecting. A caller count comfortably past that — proxying "many channels polling this one Spotify app
    /// at once" — must see at least one request rejected by the limiter rather than every single one going
    /// straight through, which is exactly the unbounded behavior that shipped before this gate existed.</summary>
    [Fact]
    public async Task A_burst_far_past_the_combined_permit_and_queue_capacity_is_capped_not_unbounded()
    {
        using HttpClient client = NewClient();
        const int burst = 200; // PermitLimit(60) + QueueLimit(50) = 110 admitted at most

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
                catch (RateLimiterRejectedException)
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
}
