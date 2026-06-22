// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Billing.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Billing;

/// <summary>
/// Invite codes + founders badge (monetization-billing.md §3.4). Redemption grants the calling tenant a founders
/// badge and/or a tier (the latter via <see cref="ISubscriptionService.GrantTierAsync"/>). (Deferred —
/// documented: the <c>Channels.IsFounder</c> denormalized sync awaits that column; the badge entity is the source
/// of truth. Dedup is by the per-tenant badge row for badge-granting codes.)
/// </summary>
public sealed class InviteCodeService(
    IApplicationDbContext db,
    ISubscriptionService subscriptions,
    IEventBus eventBus,
    TimeProvider clock
) : IInviteCodeService
{
    public async Task<Result<InviteCodeValidationDto>> ValidateAsync(
        string code,
        CancellationToken ct = default
    )
    {
        InviteCode? invite = await FindByCodeAsync(code, ct);
        if (invite is null)
            return Result.Failure<InviteCodeValidationDto>("Unknown invite code.", "NOT_FOUND");

        bool valid = IsRedeemable(invite);
        return Result.Success(
            new InviteCodeValidationDto(
                valid,
                invite.Code,
                invite.GrantsFoundersBadge,
                await TierKeyAsync(invite.GrantsTierId, ct),
                Math.Max(0, invite.MaxRedemptions - invite.RedemptionCount),
                ToOffset(invite.ExpiresAt)
            )
        );
    }

    public async Task<Result<RedeemInviteCodeResultDto>> RedeemAsync(
        Guid broadcasterId,
        string code,
        CancellationToken ct = default
    )
    {
        InviteCode? invite = await FindByCodeAsync(code, ct);
        if (invite is null)
            return Result.Failure<RedeemInviteCodeResultDto>("Unknown invite code.", "NOT_FOUND");
        if (invite.ExpiresAt is DateTime expiry && expiry <= clock.GetUtcNow().UtcDateTime)
            return Result.Failure<RedeemInviteCodeResultDto>(
                "Invite code has expired.",
                "VALIDATION_FAILED"
            );
        if (invite.RedemptionCount >= invite.MaxRedemptions)
            return Result.Failure<RedeemInviteCodeResultDto>(
                "Invite code is exhausted.",
                "RATE_LIMITED"
            );
        if (
            invite.GrantsFoundersBadge
            && await db.FoundersBadges.AnyAsync(
                b => b.BroadcasterId == broadcasterId && b.InviteCode == code,
                ct
            )
        )
            return Result.Failure<RedeemInviteCodeResultDto>(
                "You have already redeemed this code.",
                "ALREADY_EXISTS"
            );

        invite.RedemptionCount++;

        FoundersBadge? badge = null;
        if (invite.GrantsFoundersBadge)
        {
            badge = new FoundersBadge
            {
                BroadcasterId = broadcasterId,
                GrantedAt = clock.GetUtcNow().UtcDateTime,
                InviteCode = code,
                IsActive = true,
            };
            db.FoundersBadges.Add(badge);
        }

        string? grantedTierKey = null;
        if (invite.GrantsTierId is Guid tierId)
        {
            Result<SubscriptionDto> grant = await subscriptions.GrantTierAsync(
                broadcasterId,
                tierId,
                isInviteOnlyGrant: true,
                ct
            );
            if (grant.IsFailure)
                return Result.Failure<RedeemInviteCodeResultDto>(
                    grant.ErrorMessage,
                    grant.ErrorCode
                );
            grantedTierKey = grant.Value.TierKey;
        }

        await db.SaveChangesAsync(ct);

        await eventBus.PublishAsync(
            new InviteCodeRedeemedEvent
            {
                BroadcasterId = broadcasterId,
                InviteCodeId = invite.Id,
                Code = code,
                GrantedFoundersBadge = badge is not null,
                GrantedTierId = invite.GrantsTierId,
            },
            ct
        );
        if (badge is not null)
            await eventBus.PublishAsync(
                new FoundersBadgeGrantedEvent
                {
                    BroadcasterId = broadcasterId,
                    FoundersBadgeId = badge.Id,
                    InviteCode = code,
                },
                ct
            );

        return Result.Success(
            new RedeemInviteCodeResultDto(badge is not null, grantedTierKey, ToBadgeDto(badge))
        );
    }

    public async Task<Result<FoundersBadgeDto?>> GetFoundersBadgeAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        FoundersBadge? badge = await db.FoundersBadges.FirstOrDefaultAsync(
            b => b.BroadcasterId == broadcasterId,
            ct
        );
        return Result.Success(ToBadgeDto(badge));
    }

    public async Task<Result<InviteCodeDto>> CreateInviteCodeAsync(
        CreateInviteCodeRequest request,
        CancellationToken ct = default
    )
    {
        if (request.MaxRedemptions <= 0)
            return Result.Failure<InviteCodeDto>(
                "MaxRedemptions must be positive.",
                "VALIDATION_FAILED"
            );
        if (
            request.GrantsTierId is Guid tierId
            && !await db.BillingTiers.AnyAsync(t => t.Id == tierId && t.DeletedAt == null, ct)
        )
            return Result.Failure<InviteCodeDto>("Granted tier not found.", "VALIDATION_FAILED");

        InviteCode invite = new()
        {
            Code = GenerateCode(),
            MaxRedemptions = request.MaxRedemptions,
            RedemptionCount = 0,
            GrantsFoundersBadge = request.GrantsFoundersBadge,
            GrantsTierId = request.GrantsTierId,
            ExpiresAt = request.ExpiresAt?.UtcDateTime,
        };
        db.InviteCodes.Add(invite);
        await db.SaveChangesAsync(ct);
        return Result.Success(await ToCodeDtoAsync(invite, ct));
    }

    public async Task<Result<PagedList<InviteCodeDto>>> ListInviteCodesAsync(
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<InviteCode> query = db.InviteCodes.Where(c => c.DeletedAt == null);
        int total = await query.CountAsync(ct);
        List<InviteCode> rows = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);
        List<InviteCodeDto> dtos = [];
        foreach (InviteCode invite in rows)
            dtos.Add(await ToCodeDtoAsync(invite, ct));
        return Result.Success(
            new PagedList<InviteCodeDto>(dtos, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result> RevokeInviteCodeAsync(
        Guid inviteCodeId,
        CancellationToken ct = default
    )
    {
        InviteCode? invite = await db.InviteCodes.FirstOrDefaultAsync(
            c => c.Id == inviteCodeId && c.DeletedAt == null,
            ct
        );
        if (invite is null)
            return Result.Failure("Invite code not found.", "NOT_FOUND");
        invite.ExpiresAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<FoundersBadgeDto>> GrantFoundersBadgeAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        FoundersBadge? badge = await db.FoundersBadges.FirstOrDefaultAsync(
            b => b.BroadcasterId == broadcasterId,
            ct
        );
        if (badge is null)
        {
            badge = new FoundersBadge
            {
                BroadcasterId = broadcasterId,
                GrantedAt = clock.GetUtcNow().UtcDateTime,
                IsActive = true,
            };
            db.FoundersBadges.Add(badge);
            await db.SaveChangesAsync(ct);
            await eventBus.PublishAsync(
                new FoundersBadgeGrantedEvent
                {
                    BroadcasterId = broadcasterId,
                    FoundersBadgeId = badge.Id,
                    InviteCode = null,
                },
                ct
            );
        }
        return Result.Success(ToBadgeDto(badge)!);
    }

    private Task<InviteCode?> FindByCodeAsync(string code, CancellationToken ct) =>
        db.InviteCodes.FirstOrDefaultAsync(c => c.Code == code && c.DeletedAt == null, ct);

    private bool IsRedeemable(InviteCode invite) =>
        invite.RedemptionCount < invite.MaxRedemptions
        && (invite.ExpiresAt is not DateTime e || e > clock.GetUtcNow().UtcDateTime);

    private async Task<string?> TierKeyAsync(Guid? tierId, CancellationToken ct) =>
        tierId is Guid id
            ? await db
                .BillingTiers.Where(t => t.Id == id)
                .Select(t => t.Key)
                .FirstOrDefaultAsync(ct)
            : null;

    private async Task<InviteCodeDto> ToCodeDtoAsync(InviteCode invite, CancellationToken ct) =>
        new(
            invite.Id,
            invite.Code,
            invite.MaxRedemptions,
            invite.RedemptionCount,
            invite.GrantsFoundersBadge,
            invite.GrantsTierId,
            await TierKeyAsync(invite.GrantsTierId, ct),
            ToOffset(invite.ExpiresAt)
        );

    private static FoundersBadgeDto? ToBadgeDto(FoundersBadge? badge) =>
        badge is null
            ? null
            : new FoundersBadgeDto(
                badge.Id,
                new DateTimeOffset(badge.GrantedAt, TimeSpan.Zero),
                badge.IsActive,
                badge.InviteCode
            );

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value is DateTime d ? new DateTimeOffset(d, TimeSpan.Zero) : null;

    private static string GenerateCode() => Convert.ToHexString(RandomNumberGenerator.GetBytes(5)); // 10 uppercase hex chars
}
