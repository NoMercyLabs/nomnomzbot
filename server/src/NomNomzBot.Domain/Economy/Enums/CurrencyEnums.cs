// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Economy.Enums;

/// <summary>
/// The engagement source an <c>EarningRule</c> accrues currency from (economy.md K.1a <c>Source</c>). Each maps
/// to an <c>earn_*</c> ledger <see cref="CurrencyEntryType"/> when currency is credited.
/// </summary>
public enum EarningSource
{
    ChatMessage,
    WatchTime,
    Follow,
    Subscription,
    GiftSubscription,
    Cheer,
    Raid,
}

/// <summary>
/// The category of a <c>CurrencyLedgerEntry</c> (economy.md K.3 <c>EntryType</c>) — what moved the balance.
/// Stored as a string column via <c>HasConversion&lt;string&gt;</c>.
/// </summary>
public enum CurrencyEntryType
{
    AdminAdjust,
    Transfer,
    EarnChat,
    EarnWatchTime,
    EarnFollow,
    EarnSubscription,
    EarnGiftSubscription,
    EarnCheer,
    EarnRaid,
    EarnPipeline,
    EarnGame,
    SpendCatalog,
    SpendGame,
    SpendPipeline,
    RefundCatalog,
    JarContribute,
    JarWithdraw,
    JarPayout,

    // Giveaways (giveaways.md D9): the entry-cost debit and the currency-prize / pot-payout credit.
    SpendGiveaway,
    EarnGiveaway,
}

/// <summary>
/// The kind of entity that sourced a <c>CurrencyLedgerEntry</c> (economy.md K.3 <c>SourceType</c>), pairing with
/// <c>SourceId</c>. Null when the entry has no originating entity (a plain admin adjust).
/// </summary>
public enum CurrencyLedgerSourceType
{
    AccountOpen,
    EarningRule,
    CatalogItem,
    GameConfig,
    SavingsJar,
    Pipeline,
    Transfer,
    Giveaway,
}
