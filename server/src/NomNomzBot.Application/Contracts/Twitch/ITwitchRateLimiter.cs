// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// Per-token adaptive rate limiter (twitch-helix.md §3.5). One bucket per token identity;
/// proactive throttle driven by the observed <c>Ratelimit-*</c> response headers; the caller
/// awaits a permit before each request and disposes the returned lease afterwards.
/// User-interactive calls are prioritised ahead of background polls.
/// </summary>
public interface ITwitchRateLimiter
{
    /// <summary>
    /// Awaits a permit for the named bucket; resolves once the bucket has remaining budget
    /// (blocking until the observed reset instant when it is exhausted). Returns a lease to
    /// dispose after the request completes.
    /// </summary>
    Task<ITwitchRateLease> AcquireAsync(
        string tokenBucketKey,
        TwitchCallPriority priority,
        CancellationToken ct = default
    );

    /// <summary>
    /// Feeds an observed response back so the bucket adapts: the remaining/reset values come
    /// from <c>Ratelimit-Remaining</c> / <c>Ratelimit-Reset</c>; a real 429 hard-blocks the
    /// bucket until <paramref name="resetsAt"/>.
    /// </summary>
    void Observe(
        string tokenBucketKey,
        int? limit,
        int? remaining,
        DateTimeOffset? resetsAt,
        bool wasHardLimited = false
    );
}

/// <summary>A held permit for one Helix call; disposed when the request completes.</summary>
public interface ITwitchRateLease : IAsyncDisposable;

/// <summary>Two-band priority so background polls never starve user-triggered calls.</summary>
public enum TwitchCallPriority
{
    Background = 0,
    UserInteractive = 1,
}
