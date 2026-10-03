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

// Economy currency events (economy.md §2). All inherit DomainEventBase (Guid EventId, DateTimeOffset
// OccurredAt, Guid BroadcasterId — inherited, never re-declared). EntryType/SourceType/Source travel as their
// string forms. The economy never invents the EventJournal row; the ledger entry references it where one exists.

/// <summary>A positive ledger entry committed (earn / jar payout / admin credit) — <c>economy.balance.credited</c>.</summary>
public sealed class CurrencyCreditedEvent : DomainEventBase
{
    /// <summary>The id of the currency account that received the currency.</summary>
    public required Guid AccountId { get; init; }

    /// <summary>The id of the viewer who owns the account.</summary>
    public required Guid ViewerUserId { get; init; }

    /// <summary>How much currency was added.</summary>
    public required long Amount { get; init; }

    /// <summary>The viewer's balance after the credit.</summary>
    public required long BalanceAfter { get; init; }

    /// <summary>The kind of ledger entry, as text.</summary>
    public required string EntryType { get; init; }

    /// <summary>What caused the credit, as text. Empty when no source is recorded.</summary>
    public required string? SourceType { get; init; }

    /// <summary>The id of the thing that caused the credit. Empty when no source is recorded.</summary>
    public required Guid? SourceId { get; init; }

    /// <summary>The id of the ledger entry that records the credit.</summary>
    public required long LedgerEntryId { get; init; }
}

/// <summary>A negative ledger entry committed (spend / jar contribute / admin debit) — <c>economy.balance.debited</c>.</summary>
public sealed class CurrencyDebitedEvent : DomainEventBase
{
    /// <summary>The id of the currency account that lost the currency.</summary>
    public required Guid AccountId { get; init; }

    /// <summary>The id of the viewer who owns the account.</summary>
    public required Guid ViewerUserId { get; init; }

    /// <summary>How much currency was taken.</summary>
    public required long Amount { get; init; }

    /// <summary>The viewer's balance after the debit.</summary>
    public required long BalanceAfter { get; init; }

    /// <summary>The kind of ledger entry, as text.</summary>
    public required string EntryType { get; init; }

    /// <summary>What caused the debit, as text. Empty when no source is recorded.</summary>
    public required string? SourceType { get; init; }

    /// <summary>The id of the thing that caused the debit. Empty when no source is recorded.</summary>
    public required Guid? SourceId { get; init; }

    /// <summary>The id of the ledger entry that records the debit.</summary>
    public required long LedgerEntryId { get; init; }
}

/// <summary>An earning rule accrued currency — <c>economy.currency.earned</c>. <c>Capped</c> when a cap clamped it.</summary>
public sealed class CurrencyEarnedEvent : DomainEventBase
{
    /// <summary>The id of the currency account that earned the currency.</summary>
    public required Guid AccountId { get; init; }

    /// <summary>The id of the viewer who earned the currency.</summary>
    public required Guid ViewerUserId { get; init; }

    /// <summary>How the viewer earned it, as text: ChatMessage, WatchTime, Follow, Subscription, GiftSubscription, Cheer, Raid or Supporter.</summary>
    public required string Source { get; init; }

    /// <summary>How much currency the viewer earned.</summary>
    public required long Amount { get; init; }

    /// <summary>True when an earning cap limited how much the viewer earned.</summary>
    public required bool Capped { get; init; }
}

/// <summary>Any ledger entry committed — <c>economy.ledger.recorded</c> (audit / projection cursor).</summary>
public sealed class LedgerEntryRecordedEvent : DomainEventBase
{
    /// <summary>The id of the ledger entry.</summary>
    public required long LedgerEntryId { get; init; }

    /// <summary>The position of the entry in this channel's ledger. It counts up with every entry.</summary>
    public required long TenantPosition { get; init; }

    /// <summary>The id of the currency account that the entry changes.</summary>
    public required Guid AccountId { get; init; }

    /// <summary>How much currency the entry moves. A negative number takes currency away.</summary>
    public required long Amount { get; init; }

    /// <summary>The kind of ledger entry, as text.</summary>
    public required string EntryType { get; init; }
}

/// <summary>A catalog purchase completed (after the debit) — <c>economy.catalog.purchased</c>.</summary>
public sealed class CatalogItemPurchasedEvent : DomainEventBase
{
    /// <summary>The id of the purchase.</summary>
    public required long PurchaseId { get; init; }

    /// <summary>The id of the store item that was bought.</summary>
    public required Guid CatalogItemId { get; init; }

    /// <summary>The id of the viewer who bought the item.</summary>
    public required Guid BuyerUserId { get; init; }

    /// <summary>The id of the currency account that paid.</summary>
    public required Guid BuyerAccountId { get; init; }

    /// <summary>How much currency the viewer paid.</summary>
    public required long CostPaid { get; init; }

    /// <summary>The kind of effect the item triggers, as text.</summary>
    public required string SinkType { get; init; }

    /// <summary>The id of the pipeline that the purchase runs. Empty when the item runs no pipeline.</summary>
    public required Guid? PipelineId { get; init; }

    /// <summary>The status of the purchase, as text.</summary>
    public required string Status { get; init; }
}

/// <summary>A purchase was refunded via a reversing ledger entry — <c>economy.catalog.refunded</c>.</summary>
public sealed class CatalogPurchaseRefundedEvent : DomainEventBase
{
    /// <summary>The id of the purchase that was refunded.</summary>
    public required long PurchaseId { get; init; }

    /// <summary>The id of the store item.</summary>
    public required Guid CatalogItemId { get; init; }

    /// <summary>The id of the viewer who gets the refund.</summary>
    public required Guid BuyerUserId { get; init; }

    /// <summary>How much currency the viewer got back.</summary>
    public required long AmountRefunded { get; init; }

    /// <summary>The id of the ledger entry that records the refund.</summary>
    public required long ReversalLedgerEntryId { get; init; }
}

/// <summary>A mini-game / gamble resolved — <c>economy.game.played</c>.</summary>
public sealed class GamePlayedEvent : DomainEventBase
{
    /// <summary>The id of this play.</summary>
    public required long GamePlayId { get; init; }

    /// <summary>The id of the game setup that was used.</summary>
    public required Guid GameConfigId { get; init; }

    /// <summary>The kind of game, as text.</summary>
    public required string GameType { get; init; }

    /// <summary>The id of the viewer who played.</summary>
    public required Guid PlayerUserId { get; init; }

    /// <summary>How much currency the viewer bet.</summary>
    public required long BetAmount { get; init; }

    /// <summary>How the play ended, as text.</summary>
    public required string Outcome { get; init; }

    /// <summary>How much currency the viewer won back.</summary>
    public required long PayoutAmount { get; init; }

    /// <summary>The payout minus the bet. A negative number means the viewer lost currency.</summary>
    public required long NetResult { get; init; }
}

/// <summary>A viewer passed the 18+ gambling gate — <c>economy.consent.age18_granted</c>.</summary>
public sealed class AgeConsentGrantedEvent : DomainEventBase
{
    /// <summary>The id of the viewer who gave consent.</summary>
    public required Guid ViewerUserId { get; init; }

    /// <summary>The id of the stored consent record.</summary>
    public required Guid ConsentRecordId { get; init; }

    /// <summary>How the viewer confirmed, as text.</summary>
    public required string ConfirmationMethod { get; init; }
}

/// <summary>A viewer revoked their 18+ consent — <c>economy.consent.age18_revoked</c>.</summary>
public sealed class AgeConsentRevokedEvent : DomainEventBase
{
    /// <summary>The id of the viewer who took back consent.</summary>
    public required Guid ViewerUserId { get; init; }

    /// <summary>The id of the consent record that was revoked.</summary>
    public required Guid ConsentRecordId { get; init; }
}

/// <summary>A jar contribution committed — <c>economy.jar.contributed</c>.</summary>
public sealed class JarContributedEvent : DomainEventBase
{
    /// <summary>The id of the savings jar.</summary>
    public required Guid JarId { get; init; }

    /// <summary>The id of the channel that gave the currency.</summary>
    public required Guid SourceBroadcasterId { get; init; }

    /// <summary>The id of the viewer who gave the currency. Empty when no single viewer gave it.</summary>
    public required Guid? ContributorUserId { get; init; }

    /// <summary>How much currency was added.</summary>
    public required long Amount { get; init; }

    /// <summary>The jar balance after the contribution.</summary>
    public required long JarBalanceAfter { get; init; }

    /// <summary>The id of the contribution record.</summary>
    public required long ContributionId { get; init; }
}

/// <summary>A jar withdrawal committed — <c>economy.jar.withdrawn</c>.</summary>
public sealed class JarWithdrawnEvent : DomainEventBase
{
    /// <summary>The id of the savings jar.</summary>
    public required Guid JarId { get; init; }

    /// <summary>The id of the channel that originally gave the currency.</summary>
    public required Guid SourceBroadcasterId { get; init; }

    /// <summary>The id of the user who took the currency out.</summary>
    public required Guid ActorUserId { get; init; }

    /// <summary>How much currency was taken out.</summary>
    public required long Amount { get; init; }

    /// <summary>The jar balance after the withdrawal.</summary>
    public required long JarBalanceAfter { get; init; }

    /// <summary>The id of the record for this withdrawal.</summary>
    public required long ContributionId { get; init; }
}

/// <summary>A contribution brought the jar to its goal — <c>economy.jar.goal_reached</c> (once per crossing).</summary>
public sealed class JarGoalReachedEvent : DomainEventBase
{
    /// <summary>The id of the savings jar.</summary>
    public required Guid JarId { get; init; }

    /// <summary>The goal amount of the jar.</summary>
    public required long GoalAmount { get; init; }

    /// <summary>The jar balance when the goal was reached.</summary>
    public required long Balance { get; init; }
}

/// <summary>A jar membership invite was created — <c>economy.jar.invite_sent</c>.</summary>
public sealed class SavingsJarInviteSentEvent : DomainEventBase
{
    /// <summary>The id of the savings jar.</summary>
    public required Guid JarId { get; init; }

    /// <summary>The id of the channel that owns the jar.</summary>
    public required Guid OwnerBroadcasterId { get; init; }

    /// <summary>The id of the channel that is invited.</summary>
    public required Guid InvitedBroadcasterId { get; init; }

    /// <summary>The role offered to the invited channel, as text.</summary>
    public required string Role { get; init; }
}

/// <summary>A jar membership was accepted or revoked — <c>economy.jar.membership_changed</c>.</summary>
public sealed class SavingsJarMembershipChangedEvent : DomainEventBase
{
    /// <summary>The id of the savings jar.</summary>
    public required Guid JarId { get; init; }

    /// <summary>The id of the channel whose membership changed.</summary>
    public required Guid MemberBroadcasterId { get; init; }

    /// <summary>The new membership status, as text.</summary>
    public required string Status { get; init; }
}
