// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Abstractions.Platform;

namespace NomNomzBot.Infrastructure.Platform;

/// <summary>
/// <see cref="IFeatureFlagCacheService"/> over the shared <see cref="ICacheService"/>. The generation lives in
/// the same cache as the verdicts, so a distributed cache invalidates every API instance at once. Per-flag
/// keys stay <c>ff:{key}:{broadcasterId}</c>, the shape the flag admin's single-key removal already uses.
/// </summary>
public sealed class FeatureFlagCacheService(ICacheService cache) : IFeatureFlagCacheService
{
    private const string GenerationKey = "ff:generation";
    private static readonly TimeSpan VerdictTtl = TimeSpan.FromSeconds(60);

    public async Task<FeatureFlagCacheRead> GetAsync(
        string flagKey,
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        string generation = await CurrentGenerationAsync(ct);
        CachedVerdict? hit = await cache.GetAsync<CachedVerdict>(Key(flagKey, broadcasterId), ct);
        bool? enabled = hit is not null && hit.Generation == generation ? hit.Enabled : null;
        return new FeatureFlagCacheRead(enabled, generation);
    }

    public Task SetAsync(
        string flagKey,
        Guid broadcasterId,
        bool enabled,
        string generation,
        CancellationToken ct = default
    ) =>
        cache.SetAsync(
            Key(flagKey, broadcasterId),
            new CachedVerdict(enabled, generation),
            VerdictTtl,
            ct
        );

    public Task InvalidateAllAsync(CancellationToken ct = default) =>
        cache.SetAsync(GenerationKey, Guid.NewGuid().ToString("N"), expiry: null, ct);

    private async Task<string> CurrentGenerationAsync(CancellationToken ct) =>
        await cache.GetAsync<string>(GenerationKey, ct) ?? string.Empty;

    private static string Key(string flagKey, Guid broadcasterId) =>
        $"ff:{flagKey}:{broadcasterId}";

    /// <summary>The stored shape: the verdict plus the generation it belongs to.</summary>
    public sealed record CachedVerdict(bool Enabled, string Generation);
}
