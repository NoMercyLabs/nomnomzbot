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

namespace NomNomzBot.Application.Abstractions.Pipeline;

/// <summary>
/// Resolves how many pipelines a channel may run at once — a resource-safety cap, not a monetization quota
/// (so it is its own concept, not an <c>IBillingTierService</c> <c>LimitedResourceRegistry</c> entry: those
/// resolve self-host to unlimited, which would let one channel starve a shared box's CPU/DB connections).
/// Self-host sizes itself from the host's own hardware and database engine (SQLite is single-writer and gets
/// a small fixed cap; Postgres self-host-full scales with CPU cores). SaaS scales by the channel's
/// subscription tier, so a Pro/Premium channel can actually use the headroom Postgres + Redis give it instead
/// of being held to the same cap as a Free channel on shared infrastructure.
/// </summary>
public interface IPipelineConcurrencyLimitProvider
{
    /// <summary>The per-channel concurrent-pipeline cap for this broadcaster right now.</summary>
    Task<int> ResolveMaxConcurrentPerChannelAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );
}
