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

namespace NomNomzBot.Application.Abstractions.Platform;

/// <summary>
/// The short-lived cache of per-channel flag verdicts. A verdict is stored with the cache generation it was
/// computed under; <see cref="InvalidateAllAsync"/> starts a new generation, so every cached verdict stops
/// counting at once. A change that moves many verdicts together (a tier edit reorders the tier floor every
/// flag gates on) calls it after it commits, instead of leaving the dashboard wrong for the cache lifetime.
/// </summary>
public interface IFeatureFlagCacheService
{
    /// <summary>
    /// The cached verdict for this flag and channel, or null on a miss. The returned generation is passed
    /// back to <see cref="SetAsync"/>, so a verdict computed before an invalidation is never stored as fresh.
    /// </summary>
    Task<FeatureFlagCacheRead> GetAsync(
        string flagKey,
        Guid broadcasterId,
        CancellationToken ct = default
    );

    Task SetAsync(
        string flagKey,
        Guid broadcasterId,
        bool enabled,
        string generation,
        CancellationToken ct = default
    );

    /// <summary>Drops every cached flag verdict for every channel.</summary>
    Task InvalidateAllAsync(CancellationToken ct = default);
}

/// <summary>One cache read: the verdict (null on a miss) and the generation it was read under.</summary>
public sealed record FeatureFlagCacheRead(bool? Enabled, string Generation);
