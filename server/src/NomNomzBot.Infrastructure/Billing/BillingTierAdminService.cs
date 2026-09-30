// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Platform;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Billing.Enums;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Billing;

/// <summary>
/// Tier authoring (S-ADMIN-4a) — the admin write surface for the tier catalogue. Editing a tier that already
/// has tenants on it follows the same counted-blast-radius shape as platform content publish
/// (<c>PlatformContentService.PublishAsync</c>): preview the live count, then apply with that count echoed
/// back; a save whose confirmed count no longer matches a freshly recomputed one fails closed rather than
/// silently applying against stale information. Every committed save drops the cached flag verdicts, because
/// flag tier floors compare tier ranks: a stale verdict would gate a feature on the old tier order.
/// </summary>
public sealed class BillingTierAdminService(
    IApplicationDbContext db,
    TimeProvider clock,
    IFeatureFlagCacheService flagCache
) : IBillingTierAdminService
{
    public async Task<Result<IReadOnlyList<TierDto>>> ListAllTiersAsync(
        CancellationToken ct = default
    )
    {
        List<BillingTier> tiers = await db
            .BillingTiers.Where(t => t.DeletedAt == null)
            .OrderBy(t => t.SortOrder)
            .ToListAsync(ct);

        List<Guid> ids = [.. tiers.Select(t => t.Id)];
        Dictionary<Guid, List<TierLimit>> limitsByTier = (
            await db
                .TierLimits.Where(l => ids.Contains(l.TierId) && l.DeletedAt == null)
                .ToListAsync(ct)
        )
            .GroupBy(l => l.TierId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return Result.Success<IReadOnlyList<TierDto>>([
            .. tiers.Select(t => ToDto(t, limitsByTier)),
        ]);
    }

    public async Task<Result<TierChangePreviewDto>> PreviewTierChangeAsync(
        Guid tierId,
        CancellationToken ct = default
    )
    {
        BillingTier? tier = await db.BillingTiers.FirstOrDefaultAsync(
            t => t.Id == tierId && t.DeletedAt == null,
            ct
        );
        if (tier is null)
            return Result.Failure<TierChangePreviewDto>("Tier not found.", "NOT_FOUND");

        return Result.Success(await ComputePreviewAsync(tierId, ct));
    }

    public async Task<Result<TierDto>> CreateTierAsync(
        CreateTierRequest request,
        Guid? actorUserId,
        CancellationToken ct = default
    )
    {
        if (
            string.IsNullOrWhiteSpace(request.Key) || string.IsNullOrWhiteSpace(request.DisplayName)
        )
            return Result.Failure<TierDto>(
                "Key and display name are required.",
                "VALIDATION_FAILED"
            );

        bool keyTaken = await db.BillingTiers.AnyAsync(
            t => t.Key == request.Key && t.DeletedAt == null,
            ct
        );
        if (keyTaken)
            return Result.Failure<TierDto>(
                $"Tier key '{request.Key}' already exists.",
                "ALREADY_EXISTS"
            );

        DateTime now = clock.GetUtcNow().UtcDateTime;
        BillingTier tier = new()
        {
            Key = request.Key,
            DisplayName = request.DisplayName,
            PriceCents = request.PriceCents,
            Currency = request.Currency,
            AllowsCustomBotName = request.AllowsCustomBotName,
            PrioritySupport = request.PrioritySupport,
            IsPublic = request.IsPublic,
            SortOrder = request.SortOrder,
        };
        db.BillingTiers.Add(tier);

        List<TierLimit> limits =
        [
            .. request.Limits.Select(l => new TierLimit
            {
                TierId = tier.Id,
                LimitKey = l.LimitKey,
                LimitValue = l.LimitValue,
            }),
        ];
        db.TierLimits.AddRange(limits);

        // A newly created tier has zero tenants on it by construction — nothing to preview, but the create
        // still lands in the audit trail like every other tier-catalogue mutation.
        db.IamAuditLogs.Add(
            new IamAuditLog
            {
                PrincipalId = actorUserId ?? Guid.Empty,
                PrincipalType = IamPrincipalType.Employee,
                Permission = "billing_tier:create",
                TargetResource = tier.Key,
                Justification =
                    $"actor={actorUserId?.ToString() ?? "system"};created tier '{tier.Key}'",
                BreakGlass = false,
                Outcome = IamOutcome.Allowed,
                OccurredAt = now,
                AffectedTenantCount = 0,
            }
        );

        await db.SaveChangesAsync(ct);
        await flagCache.InvalidateAllAsync(ct);

        return Result.Success(
            ToDto(tier, new Dictionary<Guid, List<TierLimit>> { [tier.Id] = limits })
        );
    }

    public async Task<Result<TierDto>> UpdateTierAsync(
        Guid tierId,
        UpdateTierRequest request,
        Guid? actorUserId,
        CancellationToken ct = default
    )
    {
        BillingTier? tier = await db.BillingTiers.FirstOrDefaultAsync(
            t => t.Id == tierId && t.DeletedAt == null,
            ct
        );
        if (tier is null)
            return Result.Failure<TierDto>("Tier not found.", "NOT_FOUND");

        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return Result.Failure<TierDto>("Display name is required.", "VALIDATION_FAILED");

        // Recomputed fresh, right now — never trusted from what the client sent. A stale confirmation fails
        // closed instead of silently applying against a blast radius the owner never actually saw.
        TierChangePreviewDto freshPreview = await ComputePreviewAsync(tierId, ct);
        if (freshPreview.AffectedTenantCount != request.ConfirmedAffectedTenantCount)
            return Result.Failure<TierDto>(
                "The affected-tenant count changed since the last preview. Run the tier preview again.",
                "PREVIEW_STALE"
            );

        string oldValue =
            $"price={tier.PriceCents};public={tier.IsPublic};bot_name={tier.AllowsCustomBotName};priority_support={tier.PrioritySupport}";

        tier.DisplayName = request.DisplayName;
        tier.PriceCents = request.PriceCents;
        tier.Currency = request.Currency;
        tier.AllowsCustomBotName = request.AllowsCustomBotName;
        tier.PrioritySupport = request.PrioritySupport;
        tier.IsPublic = request.IsPublic;
        tier.SortOrder = request.SortOrder;

        // Soft-deleted rows are loaded too: a key the operator removed earlier and adds back is RESTORED
        // in place rather than inserted beside its deleted twin, which the (TierId, LimitKey) unique index
        // would refuse.
        List<TierLimit> existingLimits = await db
            .TierLimits.IgnoreQueryFilters()
            .Where(l => l.TierId == tierId)
            .ToListAsync(ct);
        Dictionary<string, TierLimit> existingByKey = existingLimits.ToDictionary(l => l.LimitKey);
        HashSet<string> requestedKeys = [.. request.Limits.Select(l => l.LimitKey)];

        foreach (TierLimitDto requested in request.Limits)
        {
            if (existingByKey.TryGetValue(requested.LimitKey, out TierLimit? existing))
            {
                existing.LimitValue = requested.LimitValue;
                existing.DeletedAt = null;
            }
            else
                db.TierLimits.Add(
                    new TierLimit
                    {
                        TierId = tierId,
                        LimitKey = requested.LimitKey,
                        LimitValue = requested.LimitValue,
                    }
                );
        }

        foreach (
            TierLimit stale in existingLimits.Where(l =>
                l.DeletedAt == null && !requestedKeys.Contains(l.LimitKey)
            )
        )
            db.TierLimits.Remove(stale);

        string newValue =
            $"price={tier.PriceCents};public={tier.IsPublic};bot_name={tier.AllowsCustomBotName};priority_support={tier.PrioritySupport}";

        DateTime now = clock.GetUtcNow().UtcDateTime;
        db.IamAuditLogs.Add(
            new IamAuditLog
            {
                PrincipalId = actorUserId ?? Guid.Empty,
                PrincipalType = IamPrincipalType.Employee,
                Permission = "billing_tier:update",
                TargetResource = tier.Key,
                Justification =
                    $"actor={actorUserId?.ToString() ?? "system"};old={oldValue};new={newValue}",
                BreakGlass = false,
                Outcome = IamOutcome.Allowed,
                OccurredAt = now,
                AffectedTenantCount = freshPreview.AffectedTenantCount,
            }
        );

        await db.SaveChangesAsync(ct);
        await flagCache.InvalidateAllAsync(ct);

        List<TierLimit> finalLimits = await db
            .TierLimits.Where(l => l.TierId == tierId && l.DeletedAt == null)
            .ToListAsync(ct);
        return Result.Success(
            ToDto(tier, new Dictionary<Guid, List<TierLimit>> { [tierId] = finalLimits })
        );
    }

    /// <summary>
    /// The channels an edit to <paramref name="tierId"/> reaches: every SaaS channel whose effective tier
    /// (<see cref="EffectiveTierRule"/>) is this one. That includes channels on the base tier because they have
    /// no subscription, and channels a live comp grant lifts onto it; it excludes a subscriber a higher grant
    /// lifts away. Resolved in bulk, four queries whatever the channel count.
    /// </summary>
    private async Task<TierChangePreviewDto> ComputePreviewAsync(Guid tierId, CancellationToken ct)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;

        Dictionary<Guid, BillingTier> tiers = await db
            .BillingTiers.Where(t => t.DeletedAt == null)
            .ToDictionaryAsync(t => t.Id, ct);
        BillingTier? baseTier = tiers.Values.FirstOrDefault(t =>
            t.Key == EffectiveTierRule.BaseTierKey
        );

        Dictionary<Guid, Guid> billedTierIdByChannel = (
            await db
                .Subscriptions.Where(s =>
                    s.DeletedAt == null
                    && (
                        s.Status == SubscriptionStatus.Active
                        || s.Status == SubscriptionStatus.Trialing
                    )
                )
                .Select(s => new KeyValuePair<Guid, Guid>(s.BroadcasterId, s.TierId))
                .ToListAsync(ct)
        )
            .GroupBy(pair => pair.Key)
            .ToDictionary(group => group.Key, group => group.First().Value);

        Dictionary<Guid, BillingTier> compedTierByChannel = (
            await db
                .EntitlementGrants.Where(g => g.DeletedAt == null && g.ExpiresAt > now)
                .Select(g => new KeyValuePair<Guid, Guid>(g.BroadcasterId, g.GrantedTierId))
                .ToListAsync(ct)
        )
            .Where(pair => tiers.ContainsKey(pair.Value))
            .GroupBy(pair => pair.Key)
            .ToDictionary(
                group => group.Key,
                group => group.Select(pair => tiers[pair.Value]).MaxBy(t => t.SortOrder)!
            );

        List<TenantRow> channels = await db
            .Channels.Where(c => c.DeletedAt == null)
            .OrderBy(c => c.Name)
            .Select(c => new TenantRow(c.Id, c.Name, c.DeploymentMode))
            .ToListAsync(ct);

        List<string> affectedNames = [];
        foreach (TenantRow channel in channels)
        {
            if (EffectiveTierRule.IsSelfHost(channel.DeploymentMode))
                continue;

            BillingTier? billed = billedTierIdByChannel.TryGetValue(
                channel.Id,
                out Guid billedTierId
            )
                ? tiers.GetValueOrDefault(billedTierId)
                : baseTier;
            BillingTier? effective = EffectiveTierRule.Pick(
                billed,
                compedTierByChannel.GetValueOrDefault(channel.Id)
            );
            if (effective?.Id == tierId)
                affectedNames.Add(channel.Name);
        }

        return new TierChangePreviewDto(affectedNames.Count, [.. affectedNames.Take(10)]);
    }

    private sealed record TenantRow(Guid Id, string Name, string DeploymentMode);

    private static TierDto ToDto(
        BillingTier tier,
        IReadOnlyDictionary<Guid, List<TierLimit>> limitsByTier
    ) =>
        new(
            tier.Id,
            tier.Key,
            tier.DisplayName,
            tier.PriceCents,
            tier.Currency,
            tier.AllowsCustomBotName,
            tier.PrioritySupport,
            tier.SortOrder,
            [
                .. (
                    limitsByTier.TryGetValue(tier.Id, out List<TierLimit>? limits) ? limits : []
                ).Select(l => new TierLimitDto(l.LimitKey, l.LimitValue)),
            ],
            tier.IsPublic
        );
}
