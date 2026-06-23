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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Twitch.Events;
using Polly;

namespace NomNomzBot.Infrastructure.Platform.Resilience;

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
    /// Adds Spotify API resilience: 2 retries with exponential backoff + circuit breaker.
    /// Respects Retry-After header on 429.
    /// </summary>
    public static IHttpClientBuilder AddSpotifyResilienceHandler(this IHttpClientBuilder builder)
    {
        builder.AddResilienceHandler(
            "spotify-resilience",
            pipeline =>
            {
                // Retry: 2 attempts, exponential backoff starting at 1s, jitter
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
                                    || status == HttpStatusCode.ServiceUnavailable
                            );
                        },
                        // Honor Retry-After header from Spotify 429 responses
                        DelayGenerator = args =>
                        {
                            if (args.Outcome.Result?.StatusCode == HttpStatusCode.TooManyRequests)
                            {
                                if (
                                    args.Outcome.Result.Headers.TryGetValues(
                                        "Retry-After",
                                        out IEnumerable<string>? values
                                    ) && int.TryParse(values.FirstOrDefault(), out int retryAfter)
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
}
