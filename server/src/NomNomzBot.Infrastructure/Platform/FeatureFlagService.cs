// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Platform;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Platform;

/// <summary>
/// Evaluates feature flags with the staged-rollout precedence (rollout-updates §5): an unexpired tenant override
/// wins; else the global toggle, gated by a deterministic rollout-% bucket and the deployment-mode gate. The
/// rollout bucket uses a process-independent FNV-1a hash of <c>BroadcasterId:Key</c> — never <c>GetHashCode</c> —
/// so a channel's in/out decision is stable across instances and monotonic as the percentage climbs. Results are
/// cached (<c>ff:{key}:{broadcasterId}</c>) with a short TTL.
/// (Deferred — documented: the consent gate (per-subject, ambiguous for a channel flag), and live cache
/// invalidation on FeatureFlagChangedEvent for global changes; the TTL bounds staleness meanwhile.)
/// </summary>
public sealed class FeatureFlagService(
    IApplicationDbContext db,
    ICurrentTenantService tenant,
    ICacheService cache,
    IBillingTierService billingTiers,
    TimeProvider clock
) : IFeatureFlagService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public Task<bool> IsEnabledAsync(string flagKey, CancellationToken ct = default) =>
        tenant.BroadcasterId is Guid broadcasterId
            ? IsEnabledForAsync(flagKey, broadcasterId, ct)
            : EvaluateGlobalOnlyAsync(flagKey, ct);

    public async Task<bool> IsEnabledForAsync(
        string flagKey,
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        string cacheKey = $"ff:{flagKey}:{broadcasterId}";
        bool? cached = await cache.GetAsync<bool?>(cacheKey, ct);
        if (cached is bool hit)
            return hit;

        FeatureFlagEvaluation eval = await EvaluateAsync(flagKey, broadcasterId, ct);
        await cache.SetAsync(cacheKey, eval.Enabled, CacheTtl, ct);
        return eval.Enabled;
    }

    public async Task<FeatureFlagEvaluation> EvaluateAsync(
        string flagKey,
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        FeatureFlag? flag = await db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == flagKey, ct);
        if (flag is null)
            return new FeatureFlagEvaluation(
                Exists: false,
                Enabled: false,
                Reason: null,
                RequiredTier: null
            ); // no gate defined

        // 1. An unexpired per-tenant override is the highest-precedence input.
        DateTime now = clock.GetUtcNow().UtcDateTime;
        FeatureFlagOverride? over = await db.FeatureFlagOverrides.FirstOrDefaultAsync(
            o => o.FeatureFlagId == flag.Id && o.BroadcasterId == broadcasterId,
            ct
        );
        if (over is not null && (over.ExpiresAt is null || over.ExpiresAt > now))
            return over.IsEnabled
                ? new FeatureFlagEvaluation(true, true, null, null)
                : new FeatureFlagEvaluation(
                    true,
                    false,
                    FeatureEntitlementReason.Unavailable,
                    null
                );

        // 2. Global toggle.
        if (!flag.IsEnabledGlobally)
            return new FeatureFlagEvaluation(
                true,
                false,
                FeatureEntitlementReason.Unavailable,
                null
            );

        // 3. Deterministic rollout-% bucket.
        if (
            flag.RolloutPercentage < 100
            && Bucket(broadcasterId, flagKey) >= flag.RolloutPercentage
        )
            return new FeatureFlagEvaluation(
                true,
                false,
                FeatureEntitlementReason.Unavailable,
                null
            );

        // 4. Deployment-mode gate.
        if (
            flag.DeploymentMode is not null
            && !await DeploymentModeMatchesAsync(flag, broadcasterId, ct)
        )
            return new FeatureFlagEvaluation(
                true,
                false,
                FeatureEntitlementReason.Deployment,
                null
            );

        // 5. Tier floor — the tenant's active tier must rank at or above the flag's minimum (fail closed).
        if (flag.MinTierKey is not null)
        {
            Result<bool> atLeast = await billingTiers.IsTierAtLeastAsync(
                broadcasterId,
                flag.MinTierKey,
                ct
            );
            if (atLeast.IsFailure || !atLeast.Value)
                return new FeatureFlagEvaluation(
                    true,
                    false,
                    FeatureEntitlementReason.RequiresTier,
                    flag.MinTierKey
                );
        }

        return new FeatureFlagEvaluation(true, true, null, null);
    }

    private async Task<bool> EvaluateGlobalOnlyAsync(string flagKey, CancellationToken ct)
    {
        FeatureFlag? flag = await db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == flagKey, ct);
        return flag is { IsEnabledGlobally: true, RolloutPercentage: 100 };
    }

    private async Task<bool> DeploymentModeMatchesAsync(
        FeatureFlag flag,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        string? mode = await db
            .Channels.Where(c => c.Id == broadcasterId)
            .Select(c => c.DeploymentMode)
            .FirstOrDefaultAsync(ct);
        bool selfHost =
            mode is not null && mode.StartsWith("self_host", StringComparison.OrdinalIgnoreCase);
        return flag.DeploymentMode switch
        {
            "saas" => !selfHost,
            "self_host" => selfHost,
            _ => true,
        };
    }

    /// <summary>Stable 0–99 bucket from an FNV-1a hash of <c>BroadcasterId:Key</c> (process-independent).</summary>
    private static int Bucket(Guid broadcasterId, string flagKey)
    {
        byte[] input = Encoding.UTF8.GetBytes($"{broadcasterId}:{flagKey}");
        uint hash = 2166136261u; // FNV offset basis
        foreach (byte b in input)
        {
            hash ^= b;
            hash *= 16777619u; // FNV prime
        }
        return (int)(hash % 100u);
    }
}
