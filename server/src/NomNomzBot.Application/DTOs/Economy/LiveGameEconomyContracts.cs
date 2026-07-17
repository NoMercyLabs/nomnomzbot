// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Economy.Enums;

namespace NomNomzBot.Application.DTOs.Economy;

/// <summary>
/// One joiner's entry-fee debit for a live game session (live-games.md §3.3). <c>SessionId</c> tags the
/// ledger entry (<c>SourceType=LiveGame</c>) — the durable link crash refunds are reconstructed from.
/// </summary>
public sealed record LiveGameStakeCommand(
    Guid SessionId,
    Guid GameConfigId,
    Guid ViewerUserId,
    long Stake
);

/// <summary>
/// The posted stake: the account, the bet ledger entry (id + tenant position — the position is what a
/// refund's <c>RelatedEntryId</c> links back to), and the balance after the debit. The engine stashes this
/// in session state for settlement/refund.
/// </summary>
public sealed record LiveGameStakeResult(
    Guid AccountId,
    long BetLedgerEntryId,
    long BetTenantPosition,
    long BalanceAfter
);

/// <summary>One participant's resolved outcome inside a <see cref="LiveGameSettlement"/>.</summary>
public sealed record LiveGameSettlementAward(
    Guid ViewerUserId,
    Guid AccountId,
    long Stake,
    GameOutcome Outcome,
    long Payout,
    long? BetLedgerEntryId,
    long? BetTenantPosition
);

/// <summary>
/// The full resolution of one live game session: every participant's award, credited and recorded as
/// <c>GamePlay</c> rows (with <c>GameSessionId</c> set) by <c>IGameService.SettleLiveGameAsync</c>.
/// </summary>
public sealed record LiveGameSettlement(
    Guid SessionId,
    Guid GameConfigId,
    string GameType,
    IReadOnlyList<LiveGameSettlementAward> Awards
);

/// <summary>What a settlement actually did: rows written now, winners among them, and currency paid out.</summary>
public sealed record LiveGameSettlementResult(int SettledCount, int WinnerCount, long TotalPaidOut);
