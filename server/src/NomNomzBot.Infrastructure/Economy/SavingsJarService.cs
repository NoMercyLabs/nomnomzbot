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
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Economy.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Economy;

/// <summary>
/// Pooled cross-channel savings jars (economy.md §3.7). The membership predicate (<see cref="AcceptedMembershipAsync"/>)
/// is the cross-tenant guard — checked before every balance change. Contribute/withdraw move currency through
/// the per-channel ledger. (Deferred — documented: the caps are enforced per-movement; the per-stream
/// contribution sum and the cumulative per-channel withdrawal sum need stream context / a running tally, and
/// the history's <c>JarBalanceAfter</c> is not stored on the movement. Debits/credits are atomic on the ledger;
/// the jar balance + movement row are written right after.)
/// </summary>
public sealed class SavingsJarService(
    IApplicationDbContext db,
    ICurrencyAccountService accounts,
    IEventBus eventBus,
    TimeProvider clock
) : ISavingsJarService
{
    public async Task<Result<SavingsJarDto>> CreateJarAsync(
        Guid broadcasterId,
        CreateSavingsJarRequest request,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result.Failure<SavingsJarDto>("Jar name is required.", "VALIDATION_FAILED");

        SavingsJar jar = new()
        {
            OwnerBroadcasterId = broadcasterId,
            Name = request.Name,
            Description = request.Description,
            GoalAmount = request.GoalAmount,
            Balance = 0,
            IconUrl = request.IconUrl,
            IsOpen = request.IsOpen,
            MaxWithdrawalPerChannel = request.MaxWithdrawalPerChannel,
        };
        db.SavingsJars.Add(jar);
        db.SavingsJarMemberships.Add(
            new SavingsJarMembership
            {
                JarId = jar.Id,
                MemberBroadcasterId = broadcasterId,
                Role = JarRole.Owner,
                Status = JarMembershipStatus.Accepted,
                AcceptedAt = clock.GetUtcNow().UtcDateTime,
            }
        );
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(jar));
    }

    public async Task<Result<SavingsJarDto>> GetJarAsync(
        Guid broadcasterId,
        Guid jarId,
        CancellationToken ct = default
    )
    {
        SavingsJar? jar = await FindJarAsync(jarId, ct);
        if (jar is null)
            return Result.Failure<SavingsJarDto>("Jar not found.", "NOT_FOUND");
        if (
            jar.OwnerBroadcasterId != broadcasterId
            && await AcceptedMembershipAsync(broadcasterId, jarId, ct) is null
        )
            return Result.Failure<SavingsJarDto>(
                "No access to this jar.",
                "JAR_MEMBERSHIP_REQUIRED"
            );
        return Result.Success(ToDto(jar));
    }

    public async Task<Result<IReadOnlyList<SavingsJarDto>>> ListJarsForChannelAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        List<Guid> memberJarIds = await db
            .SavingsJarMemberships.Where(m =>
                m.MemberBroadcasterId == broadcasterId
                && m.Status == JarMembershipStatus.Accepted
                && m.DeletedAt == null
            )
            .Select(m => m.JarId)
            .ToListAsync(ct);
        List<SavingsJar> jars = await db
            .SavingsJars.Where(j =>
                j.DeletedAt == null
                && (j.OwnerBroadcasterId == broadcasterId || memberJarIds.Contains(j.Id))
            )
            .OrderBy(j => j.Name)
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<SavingsJarDto>>([.. jars.Select(ToDto)]);
    }

    public async Task<Result<SavingsJarMembershipDto>> InviteChannelAsync(
        Guid broadcasterId,
        InviteChannelRequest request,
        CancellationToken ct = default
    )
    {
        SavingsJar? jar = await FindJarAsync(request.JarId, ct);
        if (jar is null)
            return Result.Failure<SavingsJarMembershipDto>("Jar not found.", "NOT_FOUND");
        if (jar.OwnerBroadcasterId != broadcasterId)
            return Result.Failure<SavingsJarMembershipDto>(
                "Only the jar owner can invite channels.",
                "JAR_MEMBERSHIP_REQUIRED"
            );
        if (!Enum.TryParse(request.Role, ignoreCase: true, out JarRole role))
            return Result.Failure<SavingsJarMembershipDto>(
                $"Unknown jar role '{request.Role}'.",
                "VALIDATION_FAILED"
            );
        if (
            await db.SavingsJarMemberships.AnyAsync(
                m =>
                    m.JarId == request.JarId
                    && m.MemberBroadcasterId == request.InvitedBroadcasterId
                    && m.DeletedAt == null,
                ct
            )
        )
            return Result.Failure<SavingsJarMembershipDto>(
                "That channel already has a membership.",
                "ALREADY_EXISTS"
            );

        SavingsJarMembership membership = new()
        {
            JarId = request.JarId,
            MemberBroadcasterId = request.InvitedBroadcasterId,
            Role = role,
            Status = JarMembershipStatus.Pending,
            ContributionCapPerStream = request.ContributionCapPerStream,
            WithdrawalCap = request.WithdrawalCap,
            InvitedByBroadcasterId = broadcasterId,
        };
        db.SavingsJarMemberships.Add(membership);
        await db.SaveChangesAsync(ct);

        await eventBus.PublishAsync(
            new SavingsJarInviteSentEvent
            {
                BroadcasterId = broadcasterId,
                JarId = request.JarId,
                OwnerBroadcasterId = broadcasterId,
                InvitedBroadcasterId = request.InvitedBroadcasterId,
                Role = role.ToString(),
            },
            ct
        );
        return Result.Success(ToDto(membership));
    }

    public async Task<Result<SavingsJarMembershipDto>> AcceptMembershipAsync(
        Guid broadcasterId,
        Guid membershipId,
        CancellationToken ct = default
    )
    {
        SavingsJarMembership? membership = await FindMembershipAsync(membershipId, ct);
        if (membership is null)
            return Result.Failure<SavingsJarMembershipDto>("Membership not found.", "NOT_FOUND");
        if (membership.MemberBroadcasterId != broadcasterId)
            return Result.Failure<SavingsJarMembershipDto>(
                "Only the invited channel can accept.",
                "FORBIDDEN"
            );
        if (membership.Status != JarMembershipStatus.Pending)
            return Result.Failure<SavingsJarMembershipDto>(
                "Membership is not pending.",
                "VALIDATION_FAILED"
            );

        membership.Status = JarMembershipStatus.Accepted;
        membership.AcceptedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        await PublishMembershipChangedAsync(membership, ct);
        return Result.Success(ToDto(membership));
    }

    public async Task<Result> RevokeMembershipAsync(
        Guid broadcasterId,
        Guid membershipId,
        CancellationToken ct = default
    )
    {
        SavingsJarMembership? membership = await FindMembershipAsync(membershipId, ct);
        if (membership is null)
            return Result.Failure("Membership not found.", "NOT_FOUND");
        SavingsJar? jar = await FindJarAsync(membership.JarId, ct);
        bool isOwner = jar is not null && jar.OwnerBroadcasterId == broadcasterId;
        if (!isOwner && membership.MemberBroadcasterId != broadcasterId)
            return Result.Failure("Only the jar owner or the member can revoke.", "FORBIDDEN");

        membership.Status = JarMembershipStatus.Revoked;
        await db.SaveChangesAsync(ct);

        await PublishMembershipChangedAsync(membership, ct);
        return Result.Success();
    }

    public async Task<Result<JarMovementDto>> ContributeAsync(
        Guid broadcasterId,
        JarContributeRequest request,
        CancellationToken ct = default
    )
    {
        if (request.Amount <= 0)
            return Result.Failure<JarMovementDto>("Amount must be positive.", "VALIDATION_FAILED");
        SavingsJarMembership? membership = await AcceptedMembershipAsync(
            broadcasterId,
            request.JarId,
            ct
        );
        if (membership is null)
            return Result.Failure<JarMovementDto>(
                "No accepted membership in this jar.",
                "JAR_MEMBERSHIP_REQUIRED"
            );
        SavingsJar? jar = await FindJarAsync(request.JarId, ct);
        if (jar is null)
            return Result.Failure<JarMovementDto>("Jar not found.", "NOT_FOUND");
        if (!jar.IsOpen)
            return Result.Failure<JarMovementDto>("Jar is not open.", "JAR_NOT_OPEN");
        if (membership.ContributionCapPerStream is long cap && request.Amount > cap)
            return Result.Failure<JarMovementDto>(
                "Contribution exceeds the per-stream cap.",
                "JAR_CAP_EXCEEDED"
            );

        Result<CurrencyLedgerEntryDto> debit = await accounts.PostLedgerEntryAsync(
            broadcasterId,
            new PostLedgerEntryCommand(
                request.ContributorUserId,
                -request.Amount,
                nameof(CurrencyEntryType.JarContribute),
                nameof(CurrencyLedgerSourceType.SavingsJar),
                request.JarId,
                EventId: null,
                Reason: null,
                ActorUserId: null,
                IdempotencyKey: null
            ),
            ct
        );
        if (debit.IsFailure)
            return Result.Failure<JarMovementDto>(debit.ErrorMessage, debit.ErrorCode);

        bool crossedGoal =
            jar.GoalAmount is long goal
            && jar.Balance < goal
            && jar.Balance + request.Amount >= goal;
        jar.Balance += request.Amount;
        JarContribution movement = new()
        {
            JarId = request.JarId,
            SourceBroadcasterId = broadcasterId,
            ContributorAccountId = debit.Value.AccountId,
            ContributorUserId = request.ContributorUserId,
            Amount = request.Amount,
            MovementType = JarMovementType.Contribute,
            LedgerEntryId = debit.Value.Id,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.JarContributions.Add(movement);
        await db.SaveChangesAsync(ct);

        await eventBus.PublishAsync(
            new JarContributedEvent
            {
                BroadcasterId = broadcasterId,
                JarId = request.JarId,
                SourceBroadcasterId = broadcasterId,
                ContributorUserId = request.ContributorUserId,
                Amount = request.Amount,
                JarBalanceAfter = jar.Balance,
                ContributionId = movement.Id,
            },
            ct
        );
        if (crossedGoal)
            await eventBus.PublishAsync(
                new JarGoalReachedEvent
                {
                    BroadcasterId = broadcasterId,
                    JarId = request.JarId,
                    GoalAmount = jar.GoalAmount!.Value,
                    Balance = jar.Balance,
                },
                ct
            );
        return Result.Success(ToDto(movement, jar.Balance));
    }

    public async Task<Result<JarMovementDto>> WithdrawAsync(
        Guid broadcasterId,
        JarWithdrawRequest request,
        CancellationToken ct = default
    )
    {
        if (request.Amount <= 0)
            return Result.Failure<JarMovementDto>("Amount must be positive.", "VALIDATION_FAILED");
        SavingsJarMembership? membership = await AcceptedMembershipAsync(
            broadcasterId,
            request.JarId,
            ct
        );
        if (membership is null)
            return Result.Failure<JarMovementDto>(
                "No accepted membership in this jar.",
                "JAR_MEMBERSHIP_REQUIRED"
            );
        SavingsJar? jar = await FindJarAsync(request.JarId, ct);
        if (jar is null)
            return Result.Failure<JarMovementDto>("Jar not found.", "NOT_FOUND");
        if (membership.WithdrawalCap is long wcap && request.Amount > wcap)
            return Result.Failure<JarMovementDto>(
                "Withdrawal exceeds the membership cap.",
                "JAR_CAP_EXCEEDED"
            );
        if (jar.MaxWithdrawalPerChannel is long mcap && request.Amount > mcap)
            return Result.Failure<JarMovementDto>(
                "Withdrawal exceeds the jar's per-channel cap.",
                "JAR_CAP_EXCEEDED"
            );
        if (jar.Balance < request.Amount)
            return Result.Failure<JarMovementDto>(
                "Jar has insufficient balance.",
                "VALIDATION_FAILED"
            );

        Result<CurrencyLedgerEntryDto> credit = await accounts.PostLedgerEntryAsync(
            broadcasterId,
            new PostLedgerEntryCommand(
                request.TargetViewerUserId,
                request.Amount,
                nameof(CurrencyEntryType.JarWithdraw),
                nameof(CurrencyLedgerSourceType.SavingsJar),
                request.JarId,
                EventId: null,
                Reason: null,
                request.ActorUserId,
                IdempotencyKey: null
            ),
            ct
        );
        if (credit.IsFailure)
            return Result.Failure<JarMovementDto>(credit.ErrorMessage, credit.ErrorCode);

        jar.Balance -= request.Amount;
        JarContribution movement = new()
        {
            JarId = request.JarId,
            SourceBroadcasterId = broadcasterId,
            ContributorUserId = request.TargetViewerUserId,
            Amount = request.Amount,
            MovementType = JarMovementType.Withdraw,
            LedgerEntryId = credit.Value.Id,
            ActorUserId = request.ActorUserId,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.JarContributions.Add(movement);
        await db.SaveChangesAsync(ct);

        await eventBus.PublishAsync(
            new JarWithdrawnEvent
            {
                BroadcasterId = broadcasterId,
                JarId = request.JarId,
                SourceBroadcasterId = broadcasterId,
                ActorUserId = request.ActorUserId,
                Amount = request.Amount,
                JarBalanceAfter = jar.Balance,
                ContributionId = movement.Id,
            },
            ct
        );
        return Result.Success(ToDto(movement, jar.Balance));
    }

    public async Task<Result<PagedList<JarMovementDto>>> GetJarHistoryAsync(
        Guid broadcasterId,
        Guid jarId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        SavingsJar? jar = await FindJarAsync(jarId, ct);
        if (jar is null)
            return Result.Failure<PagedList<JarMovementDto>>("Jar not found.", "NOT_FOUND");
        if (
            jar.OwnerBroadcasterId != broadcasterId
            && await AcceptedMembershipAsync(broadcasterId, jarId, ct) is null
        )
            return Result.Failure<PagedList<JarMovementDto>>(
                "No access to this jar.",
                "JAR_MEMBERSHIP_REQUIRED"
            );

        IQueryable<JarContribution> query = db.JarContributions.Where(c => c.JarId == jarId);
        int total = await query.CountAsync(ct);
        List<JarContribution> rows = await query
            .OrderByDescending(c => c.Id)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);
        return Result.Success(
            new PagedList<JarMovementDto>(
                [.. rows.Select(c => ToDto(c, jarBalanceAfter: 0))], // balance-after not stored per movement
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    private Task<SavingsJar?> FindJarAsync(Guid jarId, CancellationToken ct) =>
        db.SavingsJars.FirstOrDefaultAsync(j => j.Id == jarId && j.DeletedAt == null, ct);

    private Task<SavingsJarMembership?> FindMembershipAsync(
        Guid membershipId,
        CancellationToken ct
    ) =>
        db.SavingsJarMemberships.FirstOrDefaultAsync(
            m => m.Id == membershipId && m.DeletedAt == null,
            ct
        );

    private Task<SavingsJarMembership?> AcceptedMembershipAsync(
        Guid broadcasterId,
        Guid jarId,
        CancellationToken ct
    ) =>
        db.SavingsJarMemberships.FirstOrDefaultAsync(
            m =>
                m.JarId == jarId
                && m.MemberBroadcasterId == broadcasterId
                && m.Status == JarMembershipStatus.Accepted
                && m.DeletedAt == null,
            ct
        );

    private async Task PublishMembershipChangedAsync(
        SavingsJarMembership m,
        CancellationToken ct
    ) =>
        await eventBus.PublishAsync(
            new SavingsJarMembershipChangedEvent
            {
                BroadcasterId = m.MemberBroadcasterId,
                JarId = m.JarId,
                MemberBroadcasterId = m.MemberBroadcasterId,
                Status = m.Status.ToString(),
            },
            ct
        );

    private static SavingsJarDto ToDto(SavingsJar j) =>
        new(
            j.Id,
            j.OwnerBroadcasterId,
            j.Name,
            j.Description,
            j.GoalAmount,
            j.Balance,
            j.IconUrl,
            j.IsOpen,
            j.MaxWithdrawalPerChannel,
            j.CreatedAt,
            j.UpdatedAt
        );

    private static SavingsJarMembershipDto ToDto(SavingsJarMembership m) =>
        new(
            m.Id,
            m.JarId,
            m.MemberBroadcasterId,
            m.Role.ToString(),
            m.Status.ToString(),
            m.ContributionCapPerStream,
            m.WithdrawalCap,
            m.InvitedByBroadcasterId,
            m.AcceptedAt
        );

    private static JarMovementDto ToDto(JarContribution c, long jarBalanceAfter) =>
        new(
            c.Id,
            c.JarId,
            c.SourceBroadcasterId,
            c.ContributorUserId,
            c.Amount,
            c.MovementType.ToString(),
            jarBalanceAfter,
            c.LedgerEntryId,
            c.ActorUserId,
            c.CreatedAt
        );
}
