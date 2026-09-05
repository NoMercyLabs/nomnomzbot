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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Billing;

/// <summary>
/// Comps and per-tenant entitlement grants (S-ADMIN-4b). See <see cref="IEntitlementGrantService"/> for the
/// counted-preview-then-apply contract this follows — the same shape <see cref="BillingTierAdminService"/>
/// uses for tier edits.
/// </summary>
public sealed class EntitlementGrantService(
    IApplicationDbContext db,
    IBillingTierService tiers,
    TimeProvider clock
) : IEntitlementGrantService
{
    public async Task<Result<IReadOnlyList<EntitlementGrantDto>>> ListGrantsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        List<EntitlementGrant> grants = await db
            .EntitlementGrants.Where(g =>
                g.BroadcasterId == broadcasterId && g.DeletedAt == null && g.ExpiresAt > now
            )
            .OrderByDescending(g => g.IssuedAt)
            .ToListAsync(ct);

        List<Guid> tierIds = [.. grants.Select(g => g.GrantedTierId).Distinct()];
        Dictionary<Guid, string> tierKeysById = await db
            .BillingTiers.Where(t => tierIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Key, ct);

        return Result.Success<IReadOnlyList<EntitlementGrantDto>>([
            .. grants.Select(g => ToDto(g, tierKeysById.GetValueOrDefault(g.GrantedTierId, ""))),
        ]);
    }

    public async Task<Result<EntitlementGrantPreviewDto>> PreviewGrantAsync(
        Guid broadcasterId,
        Guid tierId,
        CancellationToken ct = default
    )
    {
        bool channelExists = await db.Channels.AnyAsync(c => c.Id == broadcasterId, ct);
        if (!channelExists)
            return Result.Failure<EntitlementGrantPreviewDto>("Channel not found.", "NOT_FOUND");

        BillingTier? targetTier = await db.BillingTiers.FirstOrDefaultAsync(
            t => t.Id == tierId && t.DeletedAt == null,
            ct
        );
        if (targetTier is null)
            return Result.Failure<EntitlementGrantPreviewDto>("Tier not found.", "NOT_FOUND");

        return Result.Success(await ComputePreviewAsync(broadcasterId, targetTier, ct));
    }

    public async Task<Result<EntitlementGrantDto>> IssueGrantAsync(
        Guid broadcasterId,
        IssueEntitlementGrantRequest request,
        Guid? actorUserId,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure<EntitlementGrantDto>(
                "A reason is required to issue a grant.",
                "VALIDATION_FAILED"
            );

        DateTime now = clock.GetUtcNow().UtcDateTime;
        if (request.ExpiresAt <= now)
            return Result.Failure<EntitlementGrantDto>(
                "ExpiresAt must be in the future.",
                "VALIDATION_FAILED"
            );

        bool channelExists = await db.Channels.AnyAsync(c => c.Id == broadcasterId, ct);
        if (!channelExists)
            return Result.Failure<EntitlementGrantDto>("Channel not found.", "NOT_FOUND");

        BillingTier? targetTier = await db.BillingTiers.FirstOrDefaultAsync(
            t => t.Id == request.TierId && t.DeletedAt == null,
            ct
        );
        if (targetTier is null)
            return Result.Failure<EntitlementGrantDto>("Tier not found.", "NOT_FOUND");

        // Recomputed fresh, right now — never trusted from what the client sent. A stale confirmation fails
        // closed instead of silently comping a tenant against a blast radius the operator never actually saw.
        EntitlementGrantPreviewDto freshPreview = await ComputePreviewAsync(
            broadcasterId,
            targetTier,
            ct
        );
        if (freshPreview.ChangedLimitCount != request.ConfirmedChangedLimitCount)
            return Result.Failure<EntitlementGrantDto>(
                "The affected-limit count changed since the last preview. Run the grant preview again.",
                "PREVIEW_STALE"
            );

        EntitlementGrant grant = new()
        {
            BroadcasterId = broadcasterId,
            GrantedTierId = targetTier.Id,
            Reason = request.Reason,
            ExpiresAt = request.ExpiresAt,
            IssuedAt = now,
            IssuedByAdminId = actorUserId,
        };
        db.EntitlementGrants.Add(grant);

        db.IamAuditLogs.Add(
            new IamAuditLog
            {
                PrincipalId = actorUserId ?? Guid.Empty,
                PrincipalType = IamPrincipalType.Employee,
                Permission = "entitlement_grant:issue",
                TargetBroadcasterId = broadcasterId,
                TargetResource = targetTier.Key,
                Justification =
                    $"actor={actorUserId?.ToString() ?? "system"};tier={targetTier.Key};reason={request.Reason};expires={request.ExpiresAt:O}",
                BreakGlass = false,
                Outcome = IamOutcome.Allowed,
                OccurredAt = now,
                AffectedTenantCount = freshPreview.ChangedLimitCount,
            }
        );

        await db.SaveChangesAsync(ct);

        return Result.Success(ToDto(grant, targetTier.Key));
    }

    private async Task<EntitlementGrantPreviewDto> ComputePreviewAsync(
        Guid broadcasterId,
        BillingTier targetTier,
        CancellationToken ct
    )
    {
        Result<EntitlementDto> currentResult = await tiers.GetEntitlementAsync(broadcasterId, ct);
        IReadOnlyDictionary<string, long> currentLimits = currentResult.IsSuccess
            ? currentResult.Value.Limits
            : new Dictionary<string, long>();
        string currentTierKey = currentResult.IsSuccess ? currentResult.Value.TierKey : "";

        Dictionary<string, long> targetLimits = await db
            .TierLimits.Where(l => l.TierId == targetTier.Id && l.DeletedAt == null)
            .ToDictionaryAsync(l => l.LimitKey, l => l.LimitValue, ct);

        HashSet<string> allKeys = [.. currentLimits.Keys, .. targetLimits.Keys];
        List<string> changedKeys =
        [
            .. allKeys
                .Where(key =>
                    GetLimitOrUnlimited(currentLimits, key)
                    != GetLimitOrUnlimited(targetLimits, key)
                )
                .OrderBy(key => key, StringComparer.Ordinal),
        ];

        return new EntitlementGrantPreviewDto(
            currentTierKey,
            targetTier.Key,
            changedKeys.Count,
            changedKeys
        );
    }

    private static long GetLimitOrUnlimited(IReadOnlyDictionary<string, long> limits, string key) =>
        limits.TryGetValue(key, out long value) ? value : -1L;

    private static EntitlementGrantDto ToDto(EntitlementGrant grant, string tierKey) =>
        new(
            grant.Id,
            grant.BroadcasterId,
            grant.GrantedTierId,
            tierKey,
            grant.Reason,
            grant.ExpiresAt,
            grant.IssuedAt,
            grant.IssuedByAdminId
        );
}
