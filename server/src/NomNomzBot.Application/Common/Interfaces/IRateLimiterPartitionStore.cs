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

namespace NomNomzBot.Application.Common.Interfaces;

/// <summary>
/// The rate-limiter counter backing (platform-conventions §3.7). The host's ASP.NET Core limiter policies read
/// the window counter through this so SaaS is cluster-wide and lite is per-instance. Only the counter store is
/// abstracted; the in-box partitioned limiter glue stays in the host.
/// </summary>
public interface IRateLimiterPartitionStore
{
    /// <summary>
    /// Atomically increments the window counter for <paramref name="partitionKey"/> and reports whether the
    /// request is permitted under <paramref name="permitLimit"/> for the given window. Distributed on SaaS
    /// (Redis <c>INCR</c>+<c>EXPIRE</c>), per-instance in memory on lite.
    /// </summary>
    Task<RateLimitLease> AcquireAsync(
        string partitionKey,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken = default
    );
}

/// <summary>The outcome of a single rate-limit acquisition: whether it was permitted, the remaining budget, and the retry-after hint.</summary>
public readonly record struct RateLimitLease(bool IsAcquired, int Remaining, TimeSpan RetryAfter);
