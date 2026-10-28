// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
