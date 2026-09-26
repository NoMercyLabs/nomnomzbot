// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Twitch.Events;
using Polly;
using Polly.RateLimiting;

namespace NomNomzBot.Infrastructure.Platform.Resilience;

/// <summary>
/// The <see cref="HttpRequestMessage.Options"/> key every Spotify request carries so
/// <see cref="ResiliencePolicies.AddSpotifyResilienceHandler"/> can route it to the right rate-limit
/// partition (<see cref="SpotifyMusicProvider.SendAsync"/> sets it on every request it builds). Absent
/// (the default, <c>false</c>) means interactive — a user-triggered call, never the background poller.
/// </summary>
public static class SpotifyRequestTags
{
    public static readonly HttpRequestOptionsKey<bool> IsBackgroundPoll = new(
        "spotify.isBackgroundPoll"
    );

    /// <summary>
    /// The channel this request is made on behalf of, which IS its rate-limit partition: Spotify meters per
    /// app (client_id), and every channel must register its own Spotify app — <c>ChannelCredentialsResolver</c>
    /// fails a Spotify resolve outright rather than falling back to an app-level credential — so one channel is
    /// exactly one app is exactly one real budget. Absent means "no channel in particular", which shares a
    /// single fallback partition.
    /// </summary>
    public static readonly HttpRequestOptionsKey<Guid> BroadcasterId = new("spotify.broadcasterId");
}

/// <summary>
/// Configures Polly resilience pipelines for external HTTP clients.
/// Per spec 09-error-handling.md:
/// - Twitch: 3 retries, 500ms initial delay, 50% failure circuit breaker (30s window)
/// - Spotify: 2 retries, 1s initial delay, 50% failure circuit breaker (60s window)
/// </summary>
public static class ResiliencePolicies
{
    // Transient statuses worth a retry. 429 (TooManyRequests) is deliberately EXCLUDED: the adaptive
    // TwitchRateLimitHandler owns 429 by honouring Ratelimit-Reset, and 4xx are never retried here
    // (the only auth retry — 401-then-refresh-once — lives in the transport, not the resilience pipeline).
    private static readonly HttpStatusCode[] RetryableStatuses =
    [
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
    ];

    /// <summary>The floor for a 429 retry delay, regardless of what <c>Retry-After</c> says (including a
    /// literal 0). See <see cref="AddSpotifyResilienceHandler"/>'s retry <c>DelayGenerator</c>.</summary>
    private static readonly TimeSpan MinRetryAfterDelay = TimeSpan.FromSeconds(1);

    /// <summary>The longest <c>Retry-After</c> worth waiting out inside one request. Anything longer is handed back
    /// as the 429 so the provider starts the channel's cooling window; waiting it out inline would outlast the
    /// pipeline's total timeout and surface as a timeout instead, and cooling would never start.</summary>
    private static readonly TimeSpan MaxInlineRetryAfter = TimeSpan.FromSeconds(2);

    private static TimeSpan SpotifyRetryAfter(HttpResponseMessage response)
    {
        int seconds =
            response.Headers.TryGetValues("Retry-After", out IEnumerable<string>? values)
            && int.TryParse(values.FirstOrDefault(), out int headerValue)
                ? headerValue
                : 0;
        return TimeSpan.FromSeconds(Math.Max(seconds, MinRetryAfterDelay.Seconds));
    }

    /// <summary>
    /// Adds Twitch Helix API resilience: 3 retries (transient 5xx only) with exponential backoff + jitter,
    /// a per-request timeout, and a circuit breaker that publishes <see cref="TwitchHelixCircuitOpenedEvent"/>
    /// when it opens. 4xx are not retried; 429 is handled by the adaptive rate-limit handler.
    /// </summary>
    public static IHttpClientBuilder AddTwitchResilienceHandler(this IHttpClientBuilder builder)
    {
        builder.AddResilienceHandler(
            "twitch-resilience",
            (pipeline, context) =>
            {
                // Retry: 3 attempts, exponential backoff starting at 500ms, jitter — transient 5xx only.
                pipeline.AddRetry(
                    new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = 3,
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                        Delay = TimeSpan.FromMilliseconds(500),
                        ShouldHandle = args =>
                            ValueTask.FromResult(
                                RetryableStatuses.Contains(args.Outcome.Result?.StatusCode ?? 0)
                                    || args.Outcome.Exception is HttpRequestException
                            ),
                    }
                );

                // Per-request timeout: 10s
                pipeline.AddTimeout(TimeSpan.FromSeconds(10));

                // Circuit breaker: 50% failure rate over 30s, min 5 requests, break for 30s.
                // OnOpened surfaces the platform-level circuit-open as a domain event for observability.
                TimeSpan breakDuration = TimeSpan.FromSeconds(30);
                pipeline.AddCircuitBreaker(
                    new HttpCircuitBreakerStrategyOptions
                    {
                        FailureRatio = 0.5,
                        SamplingDuration = TimeSpan.FromSeconds(30),
                        MinimumThroughput = 5,
                        BreakDuration = breakDuration,
                        ShouldHandle = args =>
                            ValueTask.FromResult(
                                args.Outcome.Result?.StatusCode
                                    >= HttpStatusCode.InternalServerError
                                    || args.Outcome.Exception is HttpRequestException
                            ),
                        OnOpened = _ =>
                        {
                            IEventBus eventBus =
                                context.ServiceProvider.GetRequiredService<IEventBus>();
                            eventBus.PublishFireAndForget(
                                new TwitchHelixCircuitOpenedEvent
                                {
                                    BroadcasterId = Guid.Empty,
                                    ClientName = "twitch-helix",
                                    OpenedAt = TimeProvider.System.GetUtcNow(),
                                    BreakDuration = breakDuration,
                                }
                            );
                            return default;
                        },
                    }
                );
            }
        );
        return builder;
    }

    /// <summary>
    /// Adds resilience for the third-party emote provider client (chat-decoration spec §7): 3 retries (transient 5xx /
    /// network) with exponential backoff + jitter, a 10s per-attempt timeout, and a 50%/30s circuit breaker. Modelled
    /// on the Twitch handler but without the Helix-specific circuit-open event — emote warming is best-effort and the
    /// reader degrades a miss to plain text, so a rejected call simply leaves the last-good cache in place.
    /// </summary>
    public static IHttpClientBuilder AddChatEmoteResilienceHandler(this IHttpClientBuilder builder)
    {
        builder.AddResilienceHandler(
            "chat-emote-resilience",
            pipeline =>
            {
                pipeline.AddRetry(
                    new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = 3,
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                        Delay = TimeSpan.FromMilliseconds(500),
                        ShouldHandle = args =>
                            ValueTask.FromResult(
                                RetryableStatuses.Contains(args.Outcome.Result?.StatusCode ?? 0)
                                    || args.Outcome.Exception is HttpRequestException
                            ),
                    }
                );

                pipeline.AddTimeout(TimeSpan.FromSeconds(10));

                pipeline.AddCircuitBreaker(
                    new HttpCircuitBreakerStrategyOptions
                    {
                        FailureRatio = 0.5,
                        SamplingDuration = TimeSpan.FromSeconds(30),
                        MinimumThroughput = 5,
                        BreakDuration = TimeSpan.FromSeconds(30),
                        ShouldHandle = args =>
                            ValueTask.FromResult(
                                args.Outcome.Result?.StatusCode
                                    >= HttpStatusCode.InternalServerError
                                    || args.Outcome.Exception is HttpRequestException
                            ),
                    }
                );
            }
        );
        return builder;
    }

    /// <summary>
    /// Adds resilience for the alejo.io pronoun client: 3 retries (transient 5xx / network) with exponential
    /// backoff + jitter, a 10s per-attempt timeout, and a 50%/30s circuit breaker. Modelled on the chat-emote
    /// handler — the fetch is best-effort (the seeder falls back to its bundled set on failure), so a rejected
    /// call needs no domain event; it simply yields the fallback.
    /// </summary>
    public static IHttpClientBuilder AddAlejoResilienceHandler(this IHttpClientBuilder builder)
    {
        builder.AddResilienceHandler(
            "alejo-resilience",
            pipeline =>
            {
                pipeline.AddRetry(
                    new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = 3,
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                        Delay = TimeSpan.FromMilliseconds(500),
                        ShouldHandle = args =>
                            ValueTask.FromResult(
                                RetryableStatuses.Contains(args.Outcome.Result?.StatusCode ?? 0)
                                    || args.Outcome.Exception is HttpRequestException
                            ),
                    }
                );

                pipeline.AddTimeout(TimeSpan.FromSeconds(10));

                pipeline.AddCircuitBreaker(
                    new HttpCircuitBreakerStrategyOptions
                    {
                        FailureRatio = 0.5,
                        SamplingDuration = TimeSpan.FromSeconds(30),
                        MinimumThroughput = 5,
                        BreakDuration = TimeSpan.FromSeconds(30),
                        ShouldHandle = args =>
                            ValueTask.FromResult(
                                args.Outcome.Result?.StatusCode
                                    >= HttpStatusCode.InternalServerError
                                    || args.Outcome.Exception is HttpRequestException
                            ),
                    }
                );
            }
        );
        return builder;
    }

    /// <summary>
    /// Adds Discord REST API resilience (discord.md §8): 2 retries with exponential backoff + jitter on
    /// transient 5xx and 429, an 8s per-attempt timeout, and a 50%/60s circuit breaker. Honors Discord's
    /// <c>Retry-After</c> header on 429 (Discord returns it in seconds, fractional allowed) so the retry waits
    /// exactly the bucket reset rather than a blind backoff — the rate-limit contract the gateway relies on.
    /// </summary>
    public static IHttpClientBuilder AddDiscordResilienceHandler(this IHttpClientBuilder builder)
    {
        builder.AddResilienceHandler(
            "discord-resilience",
            pipeline =>
            {
                pipeline.AddRetry(
                    new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = 2,
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                        Delay = TimeSpan.FromSeconds(1),
                        ShouldHandle = args =>
                        {
                            HttpStatusCode? status = args.Outcome.Result?.StatusCode;
                            return ValueTask.FromResult(
                                status == HttpStatusCode.TooManyRequests
                                    || RetryableStatuses.Contains(status ?? 0)
                                    || args.Outcome.Exception is HttpRequestException
                            );
                        },
                        // Honor Discord's Retry-After (seconds, possibly fractional) on a 429.
                        DelayGenerator = args =>
                        {
                            if (args.Outcome.Result?.StatusCode == HttpStatusCode.TooManyRequests)
                            {
                                if (
                                    args.Outcome.Result.Headers.TryGetValues(
                                        "Retry-After",
                                        out IEnumerable<string>? values
                                    )
                                    && double.TryParse(
                                        values.FirstOrDefault(),
                                        System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        out double retryAfter
                                    )
                                )
                                {
                                    return ValueTask.FromResult<TimeSpan?>(
                                        TimeSpan.FromSeconds(retryAfter)
                                    );
                                }
                            }
                            return ValueTask.FromResult<TimeSpan?>(null); // use default backoff
                        },
                    }
                );

                // Per-request timeout: 8s
                pipeline.AddTimeout(TimeSpan.FromSeconds(8));

                // Circuit breaker: 50% failure rate over 60s, min 3 requests, break for 60s.
                pipeline.AddCircuitBreaker(
                    new HttpCircuitBreakerStrategyOptions
                    {
                        FailureRatio = 0.5,
                        SamplingDuration = TimeSpan.FromSeconds(60),
                        MinimumThroughput = 3,
                        BreakDuration = TimeSpan.FromSeconds(60),
                        ShouldHandle = args =>
                            ValueTask.FromResult(
                                args.Outcome.Result?.StatusCode
                                    >= HttpStatusCode.InternalServerError
                                    || args.Outcome.Exception is HttpRequestException
                            ),
                    }
                );
            }
        );
        return builder;
    }

    /// <summary>
    /// Adds Kick API resilience: 2 retries with exponential backoff + jitter on transient 5xx/429, an 8s
    /// per-attempt timeout, and a 50%/60s circuit breaker. Modelled on the Spotify/Discord handlers — Kick's
    /// public API is a straightforward REST surface with no documented Retry-After contract, so a 429 falls
    /// back to the same exponential backoff as a transient 5xx rather than inventing a header to honor.
    /// </summary>
    public static IHttpClientBuilder AddKickResilienceHandler(this IHttpClientBuilder builder)
    {
        builder.AddResilienceHandler(
            "kick-resilience",
            pipeline =>
            {
                pipeline.AddRetry(
                    new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = 2,
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                        Delay = TimeSpan.FromSeconds(1),
                        ShouldHandle = args =>
                        {
                            HttpStatusCode? status = args.Outcome.Result?.StatusCode;
                            return ValueTask.FromResult(
                                status == HttpStatusCode.TooManyRequests
                                    || RetryableStatuses.Contains(status ?? 0)
                                    || args.Outcome.Exception is HttpRequestException
                            );
                        },
                    }
                );

                // Per-request timeout: 8s
                pipeline.AddTimeout(TimeSpan.FromSeconds(8));

                // Circuit breaker: 50% failure rate over 60s, min 3 requests, break for 60s
                pipeline.AddCircuitBreaker(
                    new HttpCircuitBreakerStrategyOptions
                    {
                        FailureRatio = 0.5,
                        SamplingDuration = TimeSpan.FromSeconds(60),
                        MinimumThroughput = 3,
                        BreakDuration = TimeSpan.FromSeconds(60),
                        ShouldHandle = args =>
                            ValueTask.FromResult(
                                args.Outcome.Result?.StatusCode
                                    >= HttpStatusCode.InternalServerError
                                    || args.Outcome.Exception is HttpRequestException
                            ),
                    }
                );
            }
        );
        return builder;
    }

    /// <summary>
    /// Adds Spotify API resilience: a global rate limiter, 2 retries with exponential backoff, and a
    /// circuit breaker. Respects Retry-After header on 429.
    /// </summary>
    public static IHttpClientBuilder AddSpotifyResilienceHandler(this IHttpClientBuilder builder)
    {
        // ONE PAIR OF LIMITERS PER CHANNEL, not per process. Spotify meters per app (client_id) and every
        // channel registers its own Spotify app — ChannelCredentialsResolver refuses a Spotify resolve rather
        // than falling back to an app-level credential — so two channels share no real budget whatsoever.
        // A single process-wide pair therefore throttled channels against each other for nothing: one
        // streamer's poller could queue another streamer's pause behind it while both apps sat well inside
        // their own limits. Partitioned by broadcaster, each channel is metered only against the budget it
        // actually owns. Held in a dictionary declared once here rather than inside the pipeline factory so a
        // rebuilt pipeline (DI reload, test host restart) never mints a second, un-coordinated set for a
        // channel. See the doc comment below for why each channel's budget is split in two and how it's sized.
        ConcurrentDictionary<Guid, SlidingWindowRateLimiter> pollLimiters = new();
        ConcurrentDictionary<Guid, SlidingWindowRateLimiter> interactiveLimiters = new();

        static SlidingWindowRateLimiter NewLimiter(int permitLimit, int queueLimit) =>
            new(
                new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromSeconds(30),
                    SegmentsPerWindow = 6,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = queueLimit,
                }
            );

        builder.AddResilienceHandler(
            "spotify-resilience",
            pipeline =>
            {
                // Spotify's rate limit is per-app (client_id) across every user token that app holds, on a
                // rolling 30s window (developer.spotify.com/documentation/web-api/concepts/rate-limits) — not
                // per-user. This gate sits outermost (before retry/circuit-breaker) and is the ONE place ALL
                // outbound Spotify calls funnel through (poller, mutation actions, search, everything).
                //
                // TWO separate queues, not one shared FIFO: a single shared budget let MusicStatePollingService's
                // own background reads (one per connected channel, every tick it's due — see that class's own
                // cadence doc comment) queue AHEAD of a real user's pause/skip/search whenever the poller had
                // already enqueued first, confirmed live (POST .../music/pause took 9.3s end-to-end behind a
                // poller backlog; see PR conversation, and the 30s Spotify timeouts observed 2026-09-14). A busy
                // poller must never be able to starve an interactive action of budget, so each request is routed
                // by SpotifyRequestTags.IsBackgroundPoll (set on every request SpotifyMusicProvider.SendAsync
                // builds) to its OWN SlidingWindowRateLimiter with its OWN ceiling.
                //
                // Poll: sized to the poller's own formula (PollInterval — 1s per FAST-eligible channel, the
                // MusicStatePollingService's demand-aware cadence — MusicStatePollingService.IsFastPollEligible)
                // with headroom for a burst of channels going live/watched at once, but deliberately smaller
                // than before (previously 900/30s shared): total budget also came DOWN here because Spotify
                // itself was observed 429-ing this app already (2026-09-14) — the old combined ceiling assumed
                // more real per-app headroom than Spotify is actually granting right now.
                // Interactive: the larger share — a real pause/skip/search must never wait behind background
                // reads it has no reason to know about.
                //
                // An outer timeout WRAPPING the rate limiter (added first = outermost) matters on its own:
                // time spent waiting in either limiter's queue is not covered by the per-attempt 8s timeout
                // below (that one only starts once a permit is granted and the real send begins) — without
                // this, a request stuck queuing rides all the way to the raw HttpClient.Timeout default (30s,
                // ConfigureHttpClientDefaults) instead of failing fast and predictably. Set above the 8s
                // per-attempt budget so it only ever fires on a genuine queue backup, not a normal single slow
                // call.
                pipeline.AddTimeout(TimeSpan.FromSeconds(12));

                pipeline.AddRateLimiter(
                    new RateLimiterStrategyOptions
                    {
                        RateLimiter = args =>
                        {
                            HttpRequestMessage? request = args.Context.GetRequestMessage();
                            bool isBackgroundPoll =
                                request?.Options.TryGetValue(
                                    SpotifyRequestTags.IsBackgroundPoll,
                                    out bool tagged
                                ) == true
                                && tagged;
                            Guid partition =
                                request?.Options.TryGetValue(
                                    SpotifyRequestTags.BroadcasterId,
                                    out Guid broadcasterId
                                ) == true
                                    ? broadcasterId
                                    : Guid.Empty;
                            RateLimiter limiter = isBackgroundPoll
                                ? pollLimiters.GetOrAdd(partition, _ => NewLimiter(180, 20))
                                : interactiveLimiters.GetOrAdd(partition, _ => NewLimiter(270, 30));
                            return limiter.AcquireAsync(1, args.Context.CancellationToken);
                        },
                    }
                );

                // Retry: 2 attempts, exponential backoff starting at 1s, jitter. This is the ONLY layer that
                // retries a Spotify 429 — SpotifyMusicProvider.SendAsync used to retry it a second time on top
                // of this, so a single rate-limited poll could cost up to 3 real Spotify calls; that manual
                // retry is gone (SpotifyMusicProvider now only records the cooldown, never retries).
                pipeline.AddRetry(
                    new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = 2,
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                        Delay = TimeSpan.FromSeconds(1),
                        ShouldHandle = args =>
                        {
                            HttpResponseMessage? response = args.Outcome.Result;
                            HttpStatusCode? status = response?.StatusCode;
                            bool shortRateLimit =
                                status == HttpStatusCode.TooManyRequests
                                && SpotifyRetryAfter(response!) <= MaxInlineRetryAfter;
                            return ValueTask.FromResult(
                                shortRateLimit || status == HttpStatusCode.ServiceUnavailable
                            );
                        },
                        // Honor Retry-After from a Spotify 429, floored at MinRetryAfterDelay: Spotify has been
                        // observed sending "Retry-After: 0" under load, which — unfloored — retried
                        // immediately, twice, on every rate-limited poll (verified live: FailureCount never
                        // moved because each 429 "succeeded" on its zero-delay retry, so the queue looked
                        // healthy while it was actually spinning). A missing/unparseable header falls back to
                        // the same floor rather than Polly's default exponential delay, since a provider that
                        // just said "too many requests" should never be retried at the FASTEST backoff tier.
                        DelayGenerator = args =>
                        {
                            if (args.Outcome.Result?.StatusCode == HttpStatusCode.TooManyRequests)
                                return ValueTask.FromResult<TimeSpan?>(
                                    SpotifyRetryAfter(args.Outcome.Result)
                                );
                            return ValueTask.FromResult<TimeSpan?>(null); // use default backoff
                        },
                    }
                );

                // Per-request timeout: 8s
                pipeline.AddTimeout(TimeSpan.FromSeconds(8));

                // Circuit breaker: 50% failure rate over 60s, min 3 requests, break for 60s. Outage signals only
                // (5xx/network). This breaker is shared by every channel while each channel has its own Spotify
                // app and budget, so a 429 must NOT count here: one throttled channel would switch Spotify off
                // for all. A 429 is handled per channel by the provider's cooling window instead.
                pipeline.AddCircuitBreaker(
                    new HttpCircuitBreakerStrategyOptions
                    {
                        FailureRatio = 0.5,
                        SamplingDuration = TimeSpan.FromSeconds(60),
                        MinimumThroughput = 3,
                        BreakDuration = TimeSpan.FromSeconds(60),
                        ShouldHandle = args =>
                            ValueTask.FromResult(
                                args.Outcome.Result?.StatusCode
                                    >= HttpStatusCode.InternalServerError
                                    || args.Outcome.Exception is HttpRequestException
                            ),
                    }
                );
            }
        );
        return builder;
    }
}
