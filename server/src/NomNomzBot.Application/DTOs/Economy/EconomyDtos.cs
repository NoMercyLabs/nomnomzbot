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

/// <summary>One channel earning rule (economy.md §4). <c>Source</c> is an <c>EarningSource</c> token.</summary>
public sealed record EarningRuleDto(
    Guid Id,
    string Source,
    bool IsEnabled,
    long Rate,
    int? UnitWindowSeconds,
    long? PerWindowCap,
    long? PerStreamCap,
    int? MinRoleLevel,
    int ConfigSchemaVersion,
    IReadOnlyDictionary<string, object?>? BonusConfig
);

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

/// <summary>The outcome of applying an earning rule to one viewer (economy.md §4).</summary>
public sealed record EarnResultDto(Guid ViewerUserId, long AmountCredited, bool Capped);

/// <summary>A purchasable store item (economy.md §4).</summary>
public sealed record CatalogItemDto(
    Guid Id,
    string Name,
    string? Description,
    string SinkType,
    long Cost,
    string? IconUrl,
    bool IsEnabled,
    string Permission,
    Guid? PipelineId,
    int CooldownSeconds,
    bool CooldownPerUser,
    int? StockLimit,
    int? StockRemaining,
    int? MaxPerViewerPerStream,
    int SortOrder,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>A pooled cross-channel savings jar (economy.md §4).</summary>
public sealed record SavingsJarDto(
    Guid Id,
    Guid OwnerBroadcasterId,
    string Name,
    string? Description,
    long? GoalAmount,
    long Balance,
    string? IconUrl,
    bool IsOpen,
    long? MaxWithdrawalPerChannel,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>A channel's membership in a savings jar (economy.md §4).</summary>
public sealed record SavingsJarMembershipDto(
    Guid Id,
    Guid JarId,
    Guid MemberBroadcasterId,
    string Role,
    string Status,
    long? ContributionCapPerStream,
    long? WithdrawalCap,
    Guid? InvitedByBroadcasterId,
    DateTime? AcceptedAt
);

/// <summary>An audited jar movement (economy.md §4).</summary>
public sealed record JarMovementDto(
    long Id,
    Guid JarId,
    Guid SourceBroadcasterId,
    Guid? ContributorUserId,
    long Amount,
    string MovementType,
    long JarBalanceAfter,
    long? LedgerEntryId,
    Guid? ActorUserId,
    DateTime CreatedAt
);

/// <summary>An immutable catalog redemption record (economy.md §4).</summary>
public sealed record CatalogPurchaseDto(
    long Id,
    Guid CatalogItemId,
    Guid BuyerUserId,
    Guid BuyerAccountId,
    long CostPaid,
    string ItemNameSnapshot,
    string Status,
    long? LedgerEntryId,
    string? InputArgs,
    DateTime CreatedAt
);
