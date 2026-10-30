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
/// Invite codes + founders badge (monetization-billing.md §3.4, S-INVITE-TIER). Redemption gives the calling
/// tenant a founders badge and/or a tier gift for N months or forever — the same entitlement grant an admin gives
/// (<see cref="IEntitlementGrantService.IssueInviteGiftAsync"/>), never a subscription row. One tenant redeems one
/// code once: dedup is by the per-tenant badge row for badge-granting codes and by the grant's invite id for
/// tier-granting codes. (Deferred — documented: the <c>Channels.IsFounder</c> denormalized sync awaits that
/// column; the badge entity is the source of truth.)
/// </summary>
public sealed class InviteCodeService(
    IApplicationDbContext db,
    IEntitlementGrantService grants,
    IEventBus eventBus,
    TimeProvider clock
) : IInviteCodeService
{
    /// <summary>The longest gift length, in months, an invite may carry (the same bound an admin gift has).</summary>
    private const int MaxGrantMonths = 120;

    /// <summary>
    /// The one answer for every code a visitor cannot use — unknown, used up, expired or revoked look the same,
    /// so a guesser learns nothing about which codes exist.
    /// </summary>
    private static readonly PublicInviteDto NotUsableInvite = new(
        false,
        "This invite link is not valid or can no longer be used.",
        null,
        null,
        false
    );

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
                ToOffset(invite.ExpiresAt),
                invite.GrantsMonths
            )
        );
    }

    public async Task<Result<PublicInviteDto>> GetPublicInviteAsync(
        string code,
        CancellationToken ct = default
    )
    {
        InviteCode? invite = await FindByCodeAsync(code, ct);
        if (invite is null || !IsRedeemable(invite))
            return Result.Success(NotUsableInvite);

        if (invite.GrantsTierId is not { } tierId)
            return Result.Success(new PublicInviteDto(true, null, null, null, false));

        string? tierName = await db
            .BillingTiers.Where(t => t.Id == tierId)
            .Select(t => t.DisplayName)
            .FirstOrDefaultAsync(ct);
        return Result.Success(
            new PublicInviteDto(
                true,
                null,
                tierName,
                invite.GrantsMonths,
                invite.GrantsMonths is null
            )
        );
    }

    public async Task<Result<RedeemInviteCodeResultDto>> RedeemForOwnerAsync(
        Guid userId,
        string code,
        CancellationToken ct = default
    )
    {
        Guid? channelId = await db
            .Channels.Where(c => c.OwnerUserId == userId && c.IsOnboarded)
            .OrderBy(c => c.CreatedAt)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
        if (channelId is not { } broadcasterId)
            return Result.Failure<RedeemInviteCodeResultDto>(
                "There is no channel to redeem this invite on yet.",
                "NOT_FOUND"
            );

        return await RedeemAsync(broadcasterId, code, ct);
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
        if (invite.ExpiresAt is { } expiry && expiry <= clock.GetUtcNow().UtcDateTime)
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
            return AlreadyRedeemed();
        if (
            invite.GrantsTierId is not null
            && await db.EntitlementGrants.AnyAsync(
                g => g.BroadcasterId == broadcasterId && g.InviteCodeId == invite.Id,
                ct
            )
        )
            return AlreadyRedeemed();

        invite.RedemptionCount++;

        FoundersBadge? badge = null;
        if (invite.GrantsFoundersBadge)
        {
            badge = new()
            {
                BroadcasterId = broadcasterId,
                GrantedAt = clock.GetUtcNow().UtcDateTime,
                InviteCode = code,
                IsActive = true,
            };
            db.FoundersBadges.Add(badge);
        }

        EntitlementGrantDto? gift = null;
        if (invite.GrantsTierId is { } tierId)
        {
            // The gift's save commits the count and the badge tracked above in the same transaction.
            Result<EntitlementGrantDto> granted = await grants.IssueInviteGiftAsync(
                broadcasterId,
                tierId,
                invite.GrantsMonths,
                invite.Id,
                code,
                ct
            );
            if (granted.IsFailure)
            {
                UndoRedemption(invite, badge);
                return Result.Failure<RedeemInviteCodeResultDto>(
                    granted.ErrorMessage,
                    granted.ErrorCode
                );
            }

            gift = granted.Value;
        }
        else
        {
            await db.SaveChangesAsync(ct);
        }

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
            new RedeemInviteCodeResultDto(
                badge is not null,
                gift?.GrantedTierKey,
                ToBadgeDto(badge),
                gift?.ExpiresAt
            )
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
        if (request.GrantsMonths is { } months)
        {
            if (request.GrantsTierId is null)
                return Result.Failure<InviteCodeDto>(
                    "A gift length needs a tier to give.",
                    "VALIDATION_FAILED"
                );
            if (months is < 1 or > MaxGrantMonths)
                return Result.Failure<InviteCodeDto>(
                    $"Months must be between 1 and {MaxGrantMonths}.",
                    "VALIDATION_FAILED"
                );
        }
        if (
            request.GrantsTierId is { } tierId
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
            GrantsMonths = request.GrantsMonths,
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
            badge = new()
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

    private static Result<RedeemInviteCodeResultDto> AlreadyRedeemed() =>
        Result.Failure<RedeemInviteCodeResultDto>(
            "You have already redeemed this code.",
            "ALREADY_EXISTS"
        );

    /// <summary>A refused gift spends nothing: give the redemption back and drop the unsaved badge.</summary>
    private void UndoRedemption(InviteCode invite, FoundersBadge? badge)
    {
        invite.RedemptionCount--;
        if (badge is not null)
            db.FoundersBadges.Remove(badge);
    }

    private bool IsRedeemable(InviteCode invite) =>
        invite.RedemptionCount < invite.MaxRedemptions
        && (invite.ExpiresAt is not { } e || e > clock.GetUtcNow().UtcDateTime);

    private async Task<string?> TierKeyAsync(Guid? tierId, CancellationToken ct) =>
        tierId is { } id
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
            ToOffset(invite.ExpiresAt),
            invite.GrantsMonths
        );

    private static FoundersBadgeDto? ToBadgeDto(FoundersBadge? badge) =>
        badge is null
            ? null
            : new FoundersBadgeDto(
                badge.Id,
                new(badge.GrantedAt, TimeSpan.Zero),
                badge.IsActive,
                badge.InviteCode
            );

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value is { } d ? new DateTimeOffset(d, TimeSpan.Zero) : null;

    private static string GenerateCode() => Convert.ToHexString(RandomNumberGenerator.GetBytes(5)); // 10 uppercase hex chars
}
