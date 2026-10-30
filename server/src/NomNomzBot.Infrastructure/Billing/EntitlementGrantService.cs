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

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Platform;
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
    TimeProvider clock,
    IFeatureFlagCacheService flagCache
) : IEntitlementGrantService
{
    public async Task<Result<IReadOnlyList<EntitlementGrantDto>>> ListGrantsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        List<EntitlementGrant> grants = await db
            .EntitlementGrants.WhereLive(now)
            .Where(g => g.BroadcasterId == broadcasterId)
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
        Result<DateTime?> endDate = ResolveEndDate(request, now);
        if (endDate.IsFailure)
            return Result.Failure<EntitlementGrantDto>(endDate.ErrorMessage, endDate.ErrorCode);

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
            ExpiresAt = endDate.Value,
            IssuedAt = now,
            IssuedByAdminId = actorUserId,
        };
        db.EntitlementGrants.Add(grant);

        db.IamAuditLogs.Add(
            Audit(
                "entitlement_grant:issue",
                grant,
                targetTier.Key,
                actorUserId,
                now,
                freshPreview.ChangedLimitCount
            )
        );

        await db.SaveChangesAsync(ct);
        // A tier-gated flag caches its verdict; the gift must change what the channel can do at once.
        await flagCache.InvalidateAllAsync(ct);

        return Result.Success(ToDto(grant, targetTier.Key));
    }

    public async Task<Result<EntitlementGrantDto>> EndGrantAsync(
        Guid broadcasterId,
        Guid grantId,
        Guid? actorUserId,
        CancellationToken ct = default
    )
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        EntitlementGrant? grant = await db
            .EntitlementGrants.WhereLive(now)
            .FirstOrDefaultAsync(g => g.Id == grantId && g.BroadcasterId == broadcasterId, ct);
        if (grant is null)
            return Result.Failure<EntitlementGrantDto>("No live gift with that id.", "NOT_FOUND");

        string tierKey =
            await db
                .BillingTiers.Where(t => t.Id == grant.GrantedTierId)
                .Select(t => t.Key)
                .FirstOrDefaultAsync(ct)
            ?? "";

        grant.ExpiresAt = now;
        db.IamAuditLogs.Add(Audit("entitlement_grant:end", grant, tierKey, actorUserId, now, 0));

        await db.SaveChangesAsync(ct);
        await flagCache.InvalidateAllAsync(ct);

        return Result.Success(ToDto(grant, tierKey));
    }

    /// <summary>The longest gift a number of months may name; anything longer is a forever gift.</summary>
    private const int MaxGiftMonths = 120;

    /// <summary>
    /// The stored end date for exactly one requested length: forever is null, N months counts from now, and
    /// an explicit date (the shape older clients send) must lie in the future.
    /// </summary>
    private static Result<DateTime?> ResolveEndDate(
        IssueEntitlementGrantRequest request,
        DateTime now
    )
    {
        int lengthsGiven =
            (request.ExpiresAt is null ? 0 : 1)
            + (request.Months is null ? 0 : 1)
            + (request.Forever ? 1 : 0);
        if (lengthsGiven != 1)
            return Result.Failure<DateTime?>(
                "Pick exactly one length: an end date, a number of months, or forever.",
                "VALIDATION_FAILED"
            );

        if (request.Forever)
            return Result.Success<DateTime?>(null);

        if (request.Months is { } months)
            return months is >= 1 and <= MaxGiftMonths
                ? Result.Success<DateTime?>(now.AddMonths(months))
                : Result.Failure<DateTime?>(
                    $"Months must be between 1 and {MaxGiftMonths}.",
                    "VALIDATION_FAILED"
                );

        return request.ExpiresAt > now
            ? Result.Success(request.ExpiresAt)
            : Result.Failure<DateTime?>("ExpiresAt must be in the future.", "VALIDATION_FAILED");
    }

    private static IamAuditLog Audit(
        string permission,
        EntitlementGrant grant,
        string tierKey,
        Guid? actorUserId,
        DateTime now,
        int changedLimitCount
    ) =>
        new()
        {
            PrincipalId = actorUserId ?? Guid.Empty,
            PrincipalType = IamPrincipalType.Employee,
            Permission = permission,
            TargetBroadcasterId = grant.BroadcasterId,
            TargetResource = tierKey,
            Justification =
                $"actor={actorUserId?.ToString() ?? "system"};tier={tierKey};reason={grant.Reason};expires={grant.ExpiresAt?.ToString("O") ?? "forever"}",
            BreakGlass = false,
            Outcome = IamOutcome.Allowed,
            OccurredAt = now,
            AffectedTenantCount = changedLimitCount,
        };

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

        // The current limits already carry the channel's live overrides; they keep winning after the grant, so the
        // target side carries them too, and an overridden key is never reported as a change the grant makes.
        Dictionary<string, long> targetLimits = LiveLimitOverrides.Overlay(
            await db
                .TierLimits.Where(l => l.TierId == targetTier.Id && l.DeletedAt == null)
                .ToDictionaryAsync(l => l.LimitKey, l => l.LimitValue, ct),
            await LiveLimitOverrides.LoadAsync(db, broadcasterId, clock.GetUtcNow().UtcDateTime, ct)
        );

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
