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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Billing.Enums;

namespace NomNomzBot.Infrastructure.Billing;

/// <summary>
/// Tier catalogue + entitlement resolution (monetization-billing.md §3.2). Self-host (Channel.DeploymentMode =
/// <c>self_host_*</c>) resolves every limit to unlimited; a SaaS channel resolves through its active subscription
/// (or the <c>base</c> entry tier when none is active — grandfathered, additions only). An unseeded limit key is
/// treated as unlimited.
/// </summary>
public sealed class BillingTierService(IApplicationDbContext db) : IBillingTierService
{
    private const string SelfHostPrefix = "self_host";
    private const string BaseTierKey = "base";
    private const string SelfHostTierKey = "free";

    public async Task<Result<IReadOnlyList<TierDto>>> GetPublicTiersAsync(
        CancellationToken ct = default
    )
    {
        List<BillingTier> tiers = await db
            .BillingTiers.Where(t => t.IsPublic && t.DeletedAt == null)
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
            .. tiers.Select(t => new TierDto(
                t.Id,
                t.Key,
                t.DisplayName,
                t.PriceCents,
                t.Currency,
                t.AllowsCustomBotName,
                t.PrioritySupport,
                t.SortOrder,
                [
                    .. (limitsByTier.GetValueOrDefault(t.Id) ?? []).Select(l => new TierLimitDto(
                        l.LimitKey,
                        l.LimitValue
                    )),
                ]
            )),
        ]);
    }

    public async Task<Result<EntitlementDto>> GetEntitlementAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        if (await IsSelfHostAsync(broadcasterId, ct))
        {
            List<string> keys = await db
                .TierLimits.Select(l => l.LimitKey)
                .Distinct()
                .ToListAsync(ct);
            return Result.Success(
                new EntitlementDto(
                    SelfHostTierKey,
                    AllowsCustomBotName: true,
                    PrioritySupport: false,
                    keys.ToDictionary(k => k, _ => -1L)
                )
            );
        }

        BillingTier? tier = await ResolveTierAsync(broadcasterId, ct);
        if (tier is null)
            return Result.Failure<EntitlementDto>("No billing tier configured.", "NOT_FOUND");

        Dictionary<string, long> limits = await db
            .TierLimits.Where(l => l.TierId == tier.Id && l.DeletedAt == null)
            .ToDictionaryAsync(l => l.LimitKey, l => l.LimitValue, ct);
        return Result.Success(
            new EntitlementDto(tier.Key, tier.AllowsCustomBotName, tier.PrioritySupport, limits)
        );
    }

    public async Task<Result<long>> GetLimitAsync(
        Guid broadcasterId,
        string limitKey,
        CancellationToken ct = default
    )
    {
        Result<EntitlementDto> entitlement = await GetEntitlementAsync(broadcasterId, ct);
        if (entitlement.IsFailure)
            return Result.Failure<long>(entitlement.ErrorMessage, entitlement.ErrorCode);
        // An unseeded key is unlimited (the subsystem owning it is not gated yet).
        return Result.Success(
            entitlement.Value.Limits.TryGetValue(limitKey, out long value) ? value : -1L
        );
    }

    public async Task<Result<bool>> IsTierAtLeastAsync(
        Guid broadcasterId,
        string requiredTierKey,
        CancellationToken ct = default
    )
    {
        if (await IsSelfHostAsync(broadcasterId, ct))
            return Result.Success(true); // self-host (unlimited) ranks above any tier

        BillingTier? required = await db.BillingTiers.FirstOrDefaultAsync(
            t => t.Key == requiredTierKey && t.DeletedAt == null,
            ct
        );
        if (required is null)
            return Result.Failure<bool>($"Unknown tier '{requiredTierKey}'.", "NOT_FOUND");

        BillingTier? current = await ResolveTierAsync(broadcasterId, ct);
        return Result.Success(current is not null && current.SortOrder >= required.SortOrder);
    }

    private async Task<bool> IsSelfHostAsync(Guid broadcasterId, CancellationToken ct)
    {
        string? mode = await db
            .Channels.Where(c => c.Id == broadcasterId)
            .Select(c => c.DeploymentMode)
            .FirstOrDefaultAsync(ct);
        return mode is not null
            && mode.StartsWith(SelfHostPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<BillingTier?> ResolveTierAsync(Guid broadcasterId, CancellationToken ct)
    {
        Guid? tierId = await db
            .Subscriptions.Where(s =>
                s.BroadcasterId == broadcasterId
                && s.DeletedAt == null
                && (
                    s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing
                )
            )
            .Select(s => (Guid?)s.TierId)
            .FirstOrDefaultAsync(ct);

        return tierId is Guid id
            ? await db.BillingTiers.FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt == null, ct)
            : await db.BillingTiers.FirstOrDefaultAsync(
                t => t.Key == BaseTierKey && t.DeletedAt == null,
                ct
            );
    }
}
