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
    private static readonly HttpStatusCode[] RetryableStatuses =
    [
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
    ];

    /// <summary>
    /// Adds Twitch Helix API resilience: 3 retries with exponential backoff + circuit breaker.
    /// </summary>
    public static IHttpClientBuilder AddTwitchResilienceHandler(this IHttpClientBuilder builder)
    {
        builder.AddResilienceHandler(
            "twitch-resilience",
            pipeline =>
            {
                // Retry: 3 attempts, exponential backoff starting at 500ms, jitter
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

                // Circuit breaker: 50% failure rate over 30s, min 5 requests, break for 30s
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
