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
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Twitch.Events;

namespace NomNomzBot.Infrastructure.Platform.Transport.Helix;

/// <summary>
/// The adaptive rate-limit delegating handler (twitch-helix.md §3.5, §7). Before the request it acquires a
/// permit for the call's token bucket (blocking when the bucket is exhausted); after the response it feeds
/// the observed <c>Ratelimit-Limit</c> / <c>Ratelimit-Remaining</c> / <c>Ratelimit-Reset</c> headers back
/// to <see cref="ITwitchRateLimiter.Observe"/> so the next call throttles proactively. A real 429 hard-blocks
/// the bucket until its reset and publishes <see cref="TwitchHelixRateLimitedEvent"/> with
/// <c>WasHardLimited = true</c>. The bucket key + priority travel on the request options set by the transport.
/// </summary>
public sealed class TwitchRateLimitHandler(
    ITwitchRateLimiter rateLimiter,
    IEventBus eventBus,
    TimeProvider timeProvider
) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        // No bucket key ⇒ a non-Helix request slipped through; pass straight through.
        if (
            !request.Options.TryGetValue(HelixRequestOptions.TokenBucketKey, out string? bucketKey)
            || string.IsNullOrEmpty(bucketKey)
        )
        {
            return await base.SendAsync(request, cancellationToken);
        }

        request.Options.TryGetValue(HelixRequestOptions.BroadcasterId, out Guid? broadcasterId);

        await using ITwitchRateLease lease = await rateLimiter.AcquireAsync(
            bucketKey,
            TwitchCallPriority.Background,
            cancellationToken
        );

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

        (int? limit, int? remaining, DateTimeOffset? resetsAt) = ReadRateLimitHeaders(response);
        bool hardLimited = response.StatusCode == HttpStatusCode.TooManyRequests;

        rateLimiter.Observe(bucketKey, limit, remaining, resetsAt, hardLimited);

        if (hardLimited)
        {
            await eventBus.PublishAsync(
                new TwitchHelixRateLimitedEvent
                {
                    BroadcasterId = broadcasterId ?? Guid.Empty,
                    TokenBucketKey = bucketKey,
                    RemainingBeforeThrottle = remaining ?? 0,
                    ResetsAt = resetsAt ?? timeProvider.GetUtcNow(),
                    WasHardLimited = true,
                },
                cancellationToken
            );
        }

        return response;
    }

    private static (int? Limit, int? Remaining, DateTimeOffset? ResetsAt) ReadRateLimitHeaders(
        HttpResponseMessage response
    )
    {
        int? limit = ParseInt(response, "Ratelimit-Limit");
        int? remaining = ParseInt(response, "Ratelimit-Remaining");

        DateTimeOffset? resetsAt = null;
        if (
            response.Headers.TryGetValues("Ratelimit-Reset", out IEnumerable<string>? resetValues)
            && long.TryParse(resetValues.FirstOrDefault(), out long resetEpoch)
        )
        {
            resetsAt = DateTimeOffset.FromUnixTimeSeconds(resetEpoch);
        }

        return (limit, remaining, resetsAt);
    }

    private static int? ParseInt(HttpResponseMessage response, string header) =>
        response.Headers.TryGetValues(header, out IEnumerable<string>? values)
        && int.TryParse(values.FirstOrDefault(), out int parsed)
            ? parsed
            : null;
}
