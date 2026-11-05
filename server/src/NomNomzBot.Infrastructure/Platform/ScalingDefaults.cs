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

using NomNomzBot.Domain.Enums.Deployment;

namespace NomNomzBot.Infrastructure.Platform;

/// <summary>
/// Pure, DI-free self-host concurrency sizing, shared by the per-channel pipeline cap
/// (<c>PipelineConcurrencyLimitProvider</c>, scoped — needs billing for SaaS) and the instance-wide
/// notification-dispatch bound (<c>TwitchEventSubHostedService</c>, singleton — must never depend on a
/// scoped service). Kept static so both can call it without a shared service whose lifetime would have to
/// satisfy both callers.
/// </summary>
public static class ScalingDefaults
{
    /// <summary>SQLite is single-writer: more concurrent pipelines buys lock contention, not throughput.</summary>
    public const int SelfHostLiteMaxConcurrentPerChannel = 3;

    /// <summary>Postgres self-host-full has its own connection pool and no single-writer lock, so CPU cores
    /// buy real headroom — but still capped, so one channel's chat storm can't starve the box.</summary>
    public const int SelfHostFullMaxConcurrentPerChannelCeiling = 16;

    /// <summary>
    /// The self-host per-channel pipeline cap: an explicit override always wins; otherwise SQLite gets the
    /// small fixed floor and Postgres scales with <see cref="Environment.ProcessorCount"/> up to the ceiling.
    /// </summary>
    public static int ResolveSelfHostMaxConcurrentPerChannel(
        DbProviderKind? provider,
        int? configuredOverride
    )
    {
        if (configuredOverride is { } overridden)
            return overridden;

        return provider == DbProviderKind.Postgres
            ? Math.Clamp(
                Environment.ProcessorCount,
                SelfHostLiteMaxConcurrentPerChannel,
                SelfHostFullMaxConcurrentPerChannelCeiling
            )
            : SelfHostLiteMaxConcurrentPerChannel;
    }
}
