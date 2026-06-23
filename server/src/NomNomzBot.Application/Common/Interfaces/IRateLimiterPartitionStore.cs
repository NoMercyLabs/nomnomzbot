// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
