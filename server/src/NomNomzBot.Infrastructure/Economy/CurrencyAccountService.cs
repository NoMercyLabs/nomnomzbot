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
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Economy.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Economy;

/// <summary>
/// The wallet + ledger core (economy.md §3.2). Every balance change flows through
/// <see cref="PostLedgerEntryAsync"/>, which appends one immutable entry and updates the projection atomically
/// under the caller-owned transaction, drawing a gap-free per-tenant position from the sequence allocator.
/// (<c>ViewerTwitchUserId</c> — a non-load-bearing PII-display cache — is enriched by the engagement callers;
/// the ledger math never depends on it.)
/// </summary>
public sealed class CurrencyAccountService(
    IApplicationDbContext db,
    ITenantSequenceAllocator allocator,
    IUnitOfWork unitOfWork,
    IEventBus eventBus,
    TimeProvider clock
) : ICurrencyAccountService
{
    private const string LedgerSequence = "currency_ledger_position";

    public async Task<Result<CurrencyAccountDto>> GetOrCreateAccountAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        CancellationToken ct = default
    )
    {
        CurrencyAccount? existing = await FindAccountAsync(broadcasterId, viewerUserId, ct);
        if (existing is not null)
            return Result.Success(ToDto(existing));

        CurrencyConfig? config = await LoadConfigAsync(broadcasterId, ct);
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            CurrencyAccount account = await CreateAccountAsync(
                broadcasterId,
                viewerUserId,
                config,
                ct
            );
            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitTransactionAsync(ct);
            return Result.Success(ToDto(account));
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }
    }

    public async Task<Result<long>> GetBalanceAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        CancellationToken ct = default
    )
    {
        CurrencyAccount? account = await FindAccountAsync(broadcasterId, viewerUserId, ct);
        return Result.Success(account?.Balance ?? 0);
    }

    public async Task<Result<PagedList<CurrencyAccountDto>>> ListAccountsAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<CurrencyAccount> query = db.CurrencyAccounts.Where(a =>
            a.BroadcasterId == broadcasterId
        );
        int total = await query.CountAsync(ct);
        List<CurrencyAccount> rows = await query
            .OrderByDescending(a => a.Balance)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);
        return Result.Success(
            new PagedList<CurrencyAccountDto>(
                [.. rows.Select(ToDto)],
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    public async Task<Result<CurrencyLedgerEntryDto>> PostLedgerEntryAsync(
        Guid broadcasterId,
        PostLedgerEntryCommand command,
        CancellationToken ct = default
    )
    {
        if (!Enum.TryParse(command.EntryType, ignoreCase: true, out CurrencyEntryType entryType))
            return Result.Failure<CurrencyLedgerEntryDto>(
                $"Unknown entry type '{command.EntryType}'.",
                "VALIDATION_FAILED"
            );
        CurrencyLedgerSourceType? sourceType =
            command.SourceType is not null
            && Enum.TryParse(command.SourceType, ignoreCase: true, out CurrencyLedgerSourceType st)
                ? st
                : null;

        CurrencyConfig? config = await LoadConfigAsync(broadcasterId, ct);
        if (config is null || !config.IsEnabled)
            return Result.Failure<CurrencyLedgerEntryDto>(
                "Currency is disabled.",
                "CURRENCY_DISABLED"
            );

        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            CurrencyAccount account =
                await FindAccountAsync(broadcasterId, command.ViewerUserId, ct)
                ?? await CreateAccountAsync(broadcasterId, command.ViewerUserId, config, ct);

            Result<CurrencyLedgerEntry> posted = await AppendAsync(
                broadcasterId,
                account,
                command.Amount,
                entryType,
                sourceType,
                command.SourceId,
                config,
                command.RelatedEntryId,
                command.EventId,
                command.Reason,
                command.ActorUserId,
                ct
            );
            if (posted.IsFailure)
            {
                await unitOfWork.RollbackTransactionAsync(ct);
                return Result.Failure<CurrencyLedgerEntryDto>(
                    posted.ErrorMessage,
                    posted.ErrorCode
                );
            }

            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitTransactionAsync(ct);

            await PublishMovementAsync(broadcasterId, posted.Value, ct);
            return Result.Success(ToDto(posted.Value));
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }
    }

    public async Task<Result<TransferResultDto>> TransferAsync(
        Guid broadcasterId,
        TransferCommand command,
        CancellationToken ct = default
    )
    {
        if (command.Amount <= 0)
            return Result.Failure<TransferResultDto>(
                "Transfer amount must be positive.",
                "VALIDATION_FAILED"
            );
        if (command.FromViewerUserId == command.ToViewerUserId)
            return Result.Failure<TransferResultDto>(
                "Cannot transfer to the same account.",
                "VALIDATION_FAILED"
            );

        CurrencyConfig? config = await LoadConfigAsync(broadcasterId, ct);
        if (config is null || !config.IsEnabled)
            return Result.Failure<TransferResultDto>("Currency is disabled.", "CURRENCY_DISABLED");

        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            CurrencyAccount from =
                await FindAccountAsync(broadcasterId, command.FromViewerUserId, ct)
                ?? await CreateAccountAsync(broadcasterId, command.FromViewerUserId, config, ct);
            CurrencyAccount to =
                await FindAccountAsync(broadcasterId, command.ToViewerUserId, ct)
                ?? await CreateAccountAsync(broadcasterId, command.ToViewerUserId, config, ct);

            Result<CurrencyLedgerEntry> debit = await AppendAsync(
                broadcasterId,
                from,
                -command.Amount,
                CurrencyEntryType.Transfer,
                CurrencyLedgerSourceType.Transfer,
                to.Id,
                config,
                null,
                null,
                command.Reason,
                command.ActorUserId,
                ct
            );
            if (debit.IsFailure)
            {
                await unitOfWork.RollbackTransactionAsync(ct);
                return Result.Failure<TransferResultDto>(debit.ErrorMessage, debit.ErrorCode);
            }

            Result<CurrencyLedgerEntry> credit = await AppendAsync(
                broadcasterId,
                to,
                command.Amount,
                CurrencyEntryType.Transfer,
                CurrencyLedgerSourceType.Transfer,
                from.Id,
                config,
                debit.Value.TenantPosition,
                null,
                command.Reason,
                command.ActorUserId,
                ct
            );
            if (credit.IsFailure)
            {
                await unitOfWork.RollbackTransactionAsync(ct);
                return Result.Failure<TransferResultDto>(credit.ErrorMessage, credit.ErrorCode);
            }
            debit.Value.RelatedEntryId = credit.Value.TenantPosition;

            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitTransactionAsync(ct);

            await PublishMovementAsync(broadcasterId, debit.Value, ct);
            await PublishMovementAsync(broadcasterId, credit.Value, ct);
            return Result.Success(new TransferResultDto(ToDto(debit.Value), ToDto(credit.Value)));
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }
    }

    public Task<Result<CurrencyLedgerEntryDto>> AdminAdjustAsync(
        Guid broadcasterId,
        AdminAdjustCommand command,
        CancellationToken ct = default
    ) =>
        PostLedgerEntryAsync(
            broadcasterId,
            new PostLedgerEntryCommand(
                command.ViewerUserId,
                command.Amount,
                nameof(CurrencyEntryType.AdminAdjust),
                SourceType: null,
                SourceId: null,
                EventId: null,
                command.Reason,
                command.ActorUserId,
                IdempotencyKey: null
            ),
            ct
        );

    public async Task<Result<CurrencyAccountDto>> SetFrozenAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        bool frozen,
        CancellationToken ct = default
    )
    {
        CurrencyAccount? account = await FindAccountAsync(broadcasterId, viewerUserId, ct);
        if (account is null)
            return Result.Failure<CurrencyAccountDto>("No wallet for that viewer.", "NOT_FOUND");
        account.IsFrozen = frozen;
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(account));
    }

    public async Task<Result<PagedList<CurrencyLedgerEntryDto>>> GetLedgerAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<CurrencyLedgerEntry> query = db.CurrencyLedgerEntries.Where(e =>
            e.BroadcasterId == broadcasterId && e.ViewerUserId == viewerUserId
        );
        int total = await query.CountAsync(ct);
        List<CurrencyLedgerEntry> rows = await query
            .OrderByDescending(e => e.TenantPosition)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);
        return Result.Success(
            new PagedList<CurrencyLedgerEntryDto>(
                [.. rows.Select(ToDto)],
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    /// <summary>Appends one ledger entry + updates the projection within the caller's tx. Guards, allocates the position.</summary>
    private async Task<Result<CurrencyLedgerEntry>> AppendAsync(
        Guid broadcasterId,
        CurrencyAccount account,
        long amount,
        CurrencyEntryType entryType,
        CurrencyLedgerSourceType? sourceType,
        Guid? sourceId,
        CurrencyConfig config,
        long? relatedEntryId,
        Guid? eventId,
        string? reason,
        Guid? actorUserId,
        CancellationToken ct
    )
    {
        if (account.IsFrozen)
            return Result.Failure<CurrencyLedgerEntry>("Account is frozen.", "ACCOUNT_FROZEN");

        long newBalance = account.Balance + amount;
        if (amount < 0 && newBalance < 0)
            return Result.Failure<CurrencyLedgerEntry>("Insufficient funds.", "INSUFFICIENT_FUNDS");
        if (amount > 0 && config.MaxBalance is long max && newBalance > max)
            return Result.Failure<CurrencyLedgerEntry>(
                "Maximum balance exceeded.",
                "MAX_BALANCE_EXCEEDED"
            );

        long position = (await allocator.NextAsync(broadcasterId, LedgerSequence, ct)).Value;
        DateTime now = clock.GetUtcNow().UtcDateTime;
        CurrencyLedgerEntry entry = new()
        {
            BroadcasterId = broadcasterId,
            TenantPosition = position,
            AccountId = account.Id,
            ViewerUserId = account.ViewerUserId,
            ViewerTwitchUserId = account.ViewerTwitchUserId,
            Amount = amount,
            BalanceAfter = newBalance,
            EntryType = entryType,
            SourceType = sourceType,
            SourceId = sourceId,
            RelatedEntryId = relatedEntryId,
            EventId = eventId,
            Reason = reason,
            ActorUserId = actorUserId,
            CreatedAt = now,
        };
        db.CurrencyLedgerEntries.Add(entry);

        account.Balance = newBalance;
        if (amount > 0)
            account.LifetimeEarned += amount;
        else
            account.LifetimeSpent += -amount;
        account.LastActivityAt = now;

        return Result.Success(entry);
    }

    private async Task<CurrencyAccount> CreateAccountAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        CurrencyConfig? config,
        CancellationToken ct
    )
    {
        long starting = config?.StartingBalance ?? 0;
        DateTime now = clock.GetUtcNow().UtcDateTime;
        CurrencyAccount account = new()
        {
            BroadcasterId = broadcasterId,
            ViewerUserId = viewerUserId,
            ViewerTwitchUserId = string.Empty,
            Balance = starting,
            LifetimeEarned = starting > 0 ? starting : 0,
            LastActivityAt = now,
        };
        db.CurrencyAccounts.Add(account);
        await db.SaveChangesAsync(ct); // flush so account.Id is assigned for the seed entry

        long position = (await allocator.NextAsync(broadcasterId, LedgerSequence, ct)).Value;
        db.CurrencyLedgerEntries.Add(
            new CurrencyLedgerEntry
            {
                BroadcasterId = broadcasterId,
                TenantPosition = position,
                AccountId = account.Id,
                ViewerUserId = viewerUserId,
                ViewerTwitchUserId = string.Empty,
                Amount = starting,
                BalanceAfter = starting,
                EntryType = CurrencyEntryType.AdminAdjust,
                SourceType = CurrencyLedgerSourceType.AccountOpen,
                CreatedAt = now,
            }
        );
        return account;
    }

    private Task<CurrencyAccount?> FindAccountAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        CancellationToken ct
    ) =>
        db.CurrencyAccounts.FirstOrDefaultAsync(
            a => a.BroadcasterId == broadcasterId && a.ViewerUserId == viewerUserId,
            ct
        );

    private Task<CurrencyConfig?> LoadConfigAsync(Guid broadcasterId, CancellationToken ct) =>
        db.CurrencyConfigs.FirstOrDefaultAsync(c => c.BroadcasterId == broadcasterId, ct);

    private async Task PublishMovementAsync(
        Guid broadcasterId,
        CurrencyLedgerEntry entry,
        CancellationToken ct
    )
    {
        if (entry.Amount >= 0)
            await eventBus.PublishAsync(
                new CurrencyCreditedEvent
                {
                    BroadcasterId = broadcasterId,
                    AccountId = entry.AccountId,
                    ViewerUserId = entry.ViewerUserId,
                    Amount = entry.Amount,
                    BalanceAfter = entry.BalanceAfter,
                    EntryType = entry.EntryType.ToString(),
                    SourceType = entry.SourceType?.ToString(),
                    SourceId = entry.SourceId,
                    LedgerEntryId = entry.Id,
                },
                ct
            );
        else
            await eventBus.PublishAsync(
                new CurrencyDebitedEvent
                {
                    BroadcasterId = broadcasterId,
                    AccountId = entry.AccountId,
                    ViewerUserId = entry.ViewerUserId,
                    Amount = entry.Amount,
                    BalanceAfter = entry.BalanceAfter,
                    EntryType = entry.EntryType.ToString(),
                    SourceType = entry.SourceType?.ToString(),
                    SourceId = entry.SourceId,
                    LedgerEntryId = entry.Id,
                },
                ct
            );

        await eventBus.PublishAsync(
            new LedgerEntryRecordedEvent
            {
                BroadcasterId = broadcasterId,
                LedgerEntryId = entry.Id,
                TenantPosition = entry.TenantPosition,
                AccountId = entry.AccountId,
                Amount = entry.Amount,
                EntryType = entry.EntryType.ToString(),
            },
            ct
        );
    }

    private static CurrencyAccountDto ToDto(CurrencyAccount a) =>
        new(
            a.Id,
            a.ViewerUserId,
            a.ViewerTwitchUserId,
            a.Balance,
            a.LifetimeEarned,
            a.LifetimeSpent,
            a.IsFrozen,
            a.LastActivityAt
        );

    private static CurrencyLedgerEntryDto ToDto(CurrencyLedgerEntry e) =>
        new(
            e.Id,
            e.TenantPosition,
            e.AccountId,
            e.ViewerUserId,
            e.Amount,
            e.BalanceAfter,
            e.EntryType.ToString(),
            e.SourceType?.ToString(),
            e.SourceId,
            e.RelatedEntryId,
            e.EventId,
            e.Reason,
            e.ActorUserId,
            e.CreatedAt
        );
}
