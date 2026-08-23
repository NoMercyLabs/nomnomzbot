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
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
        return await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                CurrencyAccount account = await CreateAccountAsync(
                    broadcasterId,
                    viewerUserId,
                    config,
                    token
                );
                await unitOfWork.SaveChangesAsync(token);
                return Result.Success(ToDto(account));
            },
            ct
        );
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

        // Retriable unit (Npgsql's retrying strategy rejects a bare Begin/Commit); the movement event is
        // published AFTER it commits, so a retried attempt cannot double-fire it.
        Result<CurrencyLedgerEntry> posted;
        try
        {
            posted = await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    CurrencyAccount account =
                        await FindAccountAsync(broadcasterId, command.ViewerUserId, token)
                        ?? await CreateAccountAsync(
                            broadcasterId,
                            command.ViewerUserId,
                            config,
                            token
                        );

                    Result<CurrencyLedgerEntry> appended = await AppendAsync(
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
                        token
                    );
                    if (appended.IsFailure)
                        return appended;

                    await unitOfWork.SaveChangesAsync(token);
                    return appended;
                },
                ct,
                shouldCommit: appended => appended.IsSuccess
            );
        }
        catch (DbUpdateException) when (command.EventId is not null)
        {
            // Lost a concurrent-insert race — the partial unique index on (BroadcasterId, ViewerUserId,
            // EventId, EntryType) rejected a redelivered/retried earning event (S005/F12). The balance
            // mutation already made in this transaction rolls back with it, so the DB never double-credits.
            // The winning entry is already committed by the other caller — return it as-is: idempotent
            // success, not a 500, and no second credit.
            CurrencyLedgerEntry? existing = await db.CurrencyLedgerEntries.FirstOrDefaultAsync(
                e =>
                    e.BroadcasterId == broadcasterId
                    && e.ViewerUserId == command.ViewerUserId
                    && e.EventId == command.EventId
                    && e.EntryType == entryType,
                ct
            );
            if (existing is null)
                throw;
            return Result.Success(ToDto(existing));
        }

        if (posted.IsFailure)
            return Result.Failure<CurrencyLedgerEntryDto>(posted.ErrorMessage, posted.ErrorCode);

        await PublishMovementAsync(broadcasterId, posted.Value, ct);
        return Result.Success(ToDto(posted.Value));
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

        // Retriable unit; both movement events fire only after the transfer has committed.
        Result<(CurrencyLedgerEntry Debit, CurrencyLedgerEntry Credit)> transferred =
            await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    CurrencyAccount from =
                        await FindAccountAsync(broadcasterId, command.FromViewerUserId, token)
                        ?? await CreateAccountAsync(
                            broadcasterId,
                            command.FromViewerUserId,
                            config,
                            token
                        );
                    CurrencyAccount to =
                        await FindAccountAsync(broadcasterId, command.ToViewerUserId, token)
                        ?? await CreateAccountAsync(
                            broadcasterId,
                            command.ToViewerUserId,
                            config,
                            token
                        );

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
                        token
                    );
                    if (debit.IsFailure)
                        return Result.Failure<(CurrencyLedgerEntry, CurrencyLedgerEntry)>(
                            debit.ErrorMessage,
                            debit.ErrorCode
                        );

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
                        token
                    );
                    if (credit.IsFailure)
                        return Result.Failure<(CurrencyLedgerEntry, CurrencyLedgerEntry)>(
                            credit.ErrorMessage,
                            credit.ErrorCode
                        );

                    debit.Value.RelatedEntryId = credit.Value.TenantPosition;
                    await unitOfWork.SaveChangesAsync(token);
                    return Result.Success((debit.Value, credit.Value));
                },
                ct,
                shouldCommit: transfer => transfer.IsSuccess
            );

        if (transferred.IsFailure)
            return Result.Failure<TransferResultDto>(
                transferred.ErrorMessage,
                transferred.ErrorCode
            );

        (CurrencyLedgerEntry debited, CurrencyLedgerEntry credited) = transferred.Value;
        await PublishMovementAsync(broadcasterId, debited, ct);
        await PublishMovementAsync(broadcasterId, credited, ct);
        return Result.Success(new TransferResultDto(ToDto(debited), ToDto(credited)));
    }

    public Task<Result<CurrencyLedgerEntryDto>> AdminAdjustAsync(
        Guid broadcasterId,
        AdminAdjustCommand command,
        CancellationToken ct = default
    ) =>
        PostLedgerEntryAsync(
            broadcasterId,
            new(
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

    /// <summary>
    /// Appends one ledger entry and atomically applies the CAS-gated Balance update within the caller's tx.
    /// Guards, allocates the position. LifetimeEarned/LifetimeSpent are NOT written here — see the class
    /// remarks on <see cref="CurrencyBalanceProjection"/>, which owns those columns.
    /// </summary>
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

        long? maxBalance = config.MaxBalance;
        Guid accountId = account.Id;
        DateTime activityAt = clock.GetUtcNow().UtcDateTime;
        int updatedRows = await db
            .CurrencyAccounts.Where(a =>
                a.Id == accountId
                && (amount >= 0 || a.Balance + amount >= 0)
                && (amount <= 0 || maxBalance == null || a.Balance + amount <= maxBalance)
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(a => a.Balance, a => a.Balance + amount)
                        .SetProperty(a => a.LastActivityAt, activityAt),
                ct
            );

        if (updatedRows == 0)
        {
            long currentBalance = await db
                .CurrencyAccounts.Where(a => a.Id == accountId)
                .Select(a => a.Balance)
                .FirstAsync(ct);
            long attemptedBalance = currentBalance + amount;
            if (amount < 0 && attemptedBalance < 0)
                return Result.Failure<CurrencyLedgerEntry>(
                    "Insufficient funds.",
                    "INSUFFICIENT_FUNDS"
                );
            if (amount > 0 && maxBalance is { } max && attemptedBalance > max)
                return Result.Failure<CurrencyLedgerEntry>(
                    "Maximum balance exceeded.",
                    "MAX_BALANCE_EXCEEDED"
                );
            return Result.Failure<CurrencyLedgerEntry>(
                "Balance update conflicted; retry.",
                "CONCURRENCY_CONFLICT"
            );
        }

        long newBalance = await db
            .CurrencyAccounts.Where(a => a.Id == accountId)
            .AsNoTracking()
            .Select(a => a.Balance)
            .FirstAsync(ct);
        // ExecuteUpdateAsync bypasses the change tracker, so the tracked `account` instance (if this
        // DbContext already has it loaded) would otherwise keep serving its stale pre-update values to
        // any other code in this same unit of work that looks it up again (EF's identity map always
        // prefers the tracked instance over a fresh row). Sync the CLR values in place, then re-baseline
        // each property's ORIGINAL value to match — NOT via `IsModified = false` or `entry.State =
        // Unchanged`, both of which discard the pending edit and revert CurrentValue back to what it was
        // before this method ran. Rewriting OriginalValue keeps CurrentValue as the freshly-read DB value
        // while telling EF there is nothing left to save, so a later SaveChangesAsync in this same request
        // does not re-persist a value that can go stale relative to a concurrent writer the moment we step
        // outside our own transaction.
        //
        // LifetimeEarned/LifetimeSpent are NOT touched here (S004j): they are owned solely by
        // CurrencyBalanceProjection, which folds the CurrencyCreditedEvent/CurrencyDebitedEvent this method
        // appends below, atomically via its own ExecuteUpdateAsync. AppendAsync writing them here too used
        // to double-count every credit/debit once the projection caught up.
        account.Balance = newBalance;
        account.LastActivityAt = activityAt;
        if (db is DbContext dbContext)
        {
            EntityEntry<CurrencyAccount> accountEntry = dbContext.Entry(account);
            // Setting OriginalValue alone is NOT enough — EF's per-property "modified" flag is a separate,
            // already-latched bit set the moment CurrentValue diverged from OriginalValue above; rewriting
            // OriginalValue to match does not retroactively clear it. Without also clearing IsModified, a
            // later SaveChangesAsync in this same request still re-persists CurrentValue verbatim, and if a
            // concurrent writer has moved the row on since, that re-persist silently erases it — a lost
            // update. IsModified = false, set AFTER OriginalValue already equals CurrentValue, is a no-op
            // on the value (nothing to revert to) but correctly clears the flag.
            SyncWithoutPersisting(accountEntry.Property(a => a.Balance), account.Balance);
            SyncWithoutPersisting(
                accountEntry.Property(a => a.LastActivityAt),
                account.LastActivityAt
            );
        }

        long position = (await allocator.NextAsync(broadcasterId, LedgerSequence, ct)).Value;
        DateTime now = activityAt;
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
            new()
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

    /// <summary>
    /// Accepts a property's already-assigned CurrentValue as the new baseline WITHOUT letting a later
    /// SaveChangesAsync in this unit of work re-persist it. Setting only <see cref="PropertyEntry{TEntity,TProperty}.OriginalValue"/>
    /// is not sufficient: EF's per-property "modified" flag was already latched the moment CurrentValue
    /// diverged from OriginalValue, and rewriting OriginalValue does not retroactively clear it — a later
    /// SaveChangesAsync would still re-persist CurrentValue verbatim, silently erasing whatever a
    /// concurrent writer moved the row to since. Setting IsModified = false, done AFTER OriginalValue
    /// already equals CurrentValue, is a value no-op (there is nothing left to revert to) but correctly
    /// clears the flag.
    /// </summary>
    private static void SyncWithoutPersisting<TValue>(
        PropertyEntry<CurrencyAccount, TValue> property,
        TValue currentValue
    )
    {
        property.OriginalValue = currentValue;
        property.IsModified = false;
    }

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
