// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;

namespace NomNomzBot.Application.Economy.Services;

/// <summary>
/// Wallets, balance, and the append-only ledger (economy.md §3.2) — the core mutation surface. Every balance
/// change goes through <see cref="PostLedgerEntryAsync"/>, the single atomic primitive.
/// </summary>
public interface ICurrencyAccountService
{
    /// <summary>Gets the viewer's wallet, lazily creating it (Balance = StartingBalance, one seed entry) if absent.</summary>
    Task<Result<CurrencyAccountDto>> GetOrCreateAccountAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        CancellationToken ct = default
    );

    /// <summary>The viewer's current balance (the wallet projection).</summary>
    Task<Result<long>> GetBalanceAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        CancellationToken ct = default
    );

    /// <summary>Paginated wallet list for the channel (balances table), highest balance first.</summary>
    Task<Result<PagedList<CurrencyAccountDto>>> ListAccountsAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>
    /// The atomic mutation primitive. In one transaction: guards (frozen → ACCOUNT_FROZEN, currency disabled →
    /// CURRENCY_DISABLED, credit over cap → MAX_BALANCE_EXCEEDED, debit below zero → INSUFFICIENT_FUNDS); draws
    /// a gap-free <c>TenantPosition</c>; appends one ledger entry with its <c>BalanceAfter</c>; updates the
    /// wallet projection; commits; publishes Currency(Credited|Debited)Event + LedgerEntryRecordedEvent.
    /// </summary>
    Task<Result<CurrencyLedgerEntryDto>> PostLedgerEntryAsync(
        Guid broadcasterId,
        PostLedgerEntryCommand command,
        CancellationToken ct = default
    );

    /// <summary>Moves <c>Amount</c> (&gt; 0) between two accounts as a linked debit + credit pair in one transaction.</summary>
    Task<Result<TransferResultDto>> TransferAsync(
        Guid broadcasterId,
        TransferCommand command,
        CancellationToken ct = default
    );

    /// <summary>Broadcaster/admin manual credit or debit (EntryType admin_adjust), recording actor + reason.</summary>
    Task<Result<CurrencyLedgerEntryDto>> AdminAdjustAsync(
        Guid broadcasterId,
        AdminAdjustCommand command,
        CancellationToken ct = default
    );

    /// <summary>Freezes/unfreezes a wallet (anti-abuse). No ledger effect.</summary>
    Task<Result<CurrencyAccountDto>> SetFrozenAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        bool frozen,
        CancellationToken ct = default
    );

    /// <summary>Paginated ledger history for one viewer, newest first.</summary>
    Task<Result<PagedList<CurrencyLedgerEntryDto>>> GetLedgerAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        PaginationParams pagination,
        CancellationToken ct = default
    );
}
