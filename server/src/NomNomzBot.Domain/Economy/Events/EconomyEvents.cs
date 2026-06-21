// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Economy.Events;

// Economy currency events (economy.md §2). All inherit DomainEventBase (string EventId, DateTimeOffset
// Timestamp, Guid BroadcasterId — inherited, never re-declared). EntryType/SourceType/Source travel as their
// string forms. The economy never invents the EventJournal row; the ledger entry references it where one exists.

/// <summary>A positive ledger entry committed (earn / jar payout / admin credit) — <c>economy.balance.credited</c>.</summary>
public sealed class CurrencyCreditedEvent : DomainEventBase
{
    public required Guid AccountId { get; init; }
    public required Guid ViewerUserId { get; init; }
    public required long Amount { get; init; }
    public required long BalanceAfter { get; init; }
    public required string EntryType { get; init; }
    public required string? SourceType { get; init; }
    public required Guid? SourceId { get; init; }
    public required long LedgerEntryId { get; init; }
}

/// <summary>A negative ledger entry committed (spend / jar contribute / admin debit) — <c>economy.balance.debited</c>.</summary>
public sealed class CurrencyDebitedEvent : DomainEventBase
{
    public required Guid AccountId { get; init; }
    public required Guid ViewerUserId { get; init; }
    public required long Amount { get; init; }
    public required long BalanceAfter { get; init; }
    public required string EntryType { get; init; }
    public required string? SourceType { get; init; }
    public required Guid? SourceId { get; init; }
    public required long LedgerEntryId { get; init; }
}

/// <summary>An earning rule accrued currency — <c>economy.currency.earned</c>. <c>Capped</c> when a cap clamped it.</summary>
public sealed class CurrencyEarnedEvent : DomainEventBase
{
    public required Guid AccountId { get; init; }
    public required Guid ViewerUserId { get; init; }
    public required string Source { get; init; }
    public required long Amount { get; init; }
    public required bool Capped { get; init; }
}

/// <summary>Any ledger entry committed — <c>economy.ledger.recorded</c> (audit / projection cursor).</summary>
public sealed class LedgerEntryRecordedEvent : DomainEventBase
{
    public required long LedgerEntryId { get; init; }
    public required long TenantPosition { get; init; }
    public required Guid AccountId { get; init; }
    public required long Amount { get; init; }
    public required string EntryType { get; init; }
}

/// <summary>A catalog purchase completed (after the debit) — <c>economy.catalog.purchased</c>.</summary>
public sealed class CatalogItemPurchasedEvent : DomainEventBase
{
    public required long PurchaseId { get; init; }
    public required Guid CatalogItemId { get; init; }
    public required Guid BuyerUserId { get; init; }
    public required Guid BuyerAccountId { get; init; }
    public required long CostPaid { get; init; }
    public required string SinkType { get; init; }
    public required Guid? PipelineId { get; init; }
    public required string Status { get; init; }
}

/// <summary>A purchase was refunded via a reversing ledger entry — <c>economy.catalog.refunded</c>.</summary>
public sealed class CatalogPurchaseRefundedEvent : DomainEventBase
{
    public required long PurchaseId { get; init; }
    public required Guid CatalogItemId { get; init; }
    public required Guid BuyerUserId { get; init; }
    public required long AmountRefunded { get; init; }
    public required long ReversalLedgerEntryId { get; init; }
}
