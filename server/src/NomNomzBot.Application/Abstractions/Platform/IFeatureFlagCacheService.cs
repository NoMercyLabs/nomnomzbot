// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
