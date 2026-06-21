// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.DTOs.Economy;

/// <summary>A channel's currency definition (economy.md §4).</summary>
public sealed record CurrencyConfigDto(
    Guid Id,
    Guid BroadcasterId,
    string CurrencyName,
    string? CurrencyNamePlural,
    string? IconUrl,
    bool IsEnabled,
    long StartingBalance,
    long? MaxBalance,
    int DecimalPlaces,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>A viewer's wallet (economy.md §4).</summary>
public sealed record CurrencyAccountDto(
    Guid Id,
    Guid ViewerUserId,
    string ViewerTwitchUserId,
    long Balance,
    long LifetimeEarned,
    long LifetimeSpent,
    bool IsFrozen,
    DateTime? LastActivityAt
);

/// <summary>One immutable ledger movement (economy.md §4).</summary>
public sealed record CurrencyLedgerEntryDto(
    long Id,
    long TenantPosition,
    Guid AccountId,
    Guid ViewerUserId,
    long Amount,
    long BalanceAfter,
    string EntryType,
    string? SourceType,
    Guid? SourceId,
    long? RelatedEntryId,
    Guid? EventId,
    string? Reason,
    Guid? ActorUserId,
    DateTime CreatedAt
);

/// <summary>The linked debit + credit pair produced by a transfer (economy.md §4).</summary>
public sealed record TransferResultDto(CurrencyLedgerEntryDto Debit, CurrencyLedgerEntryDto Credit);
