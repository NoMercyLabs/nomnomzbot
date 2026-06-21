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
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Economy.Entities;

/// <summary>
/// An immutable record of one game play (economy.md K.9) — the bet, the resolved <c>Outcome</c>, the payout,
/// and the net result, linking the bet + payout ledger entries. APPEND-ONLY: no <c>UpdatedAt</c>/<c>DeletedAt</c>;
/// tenant-scoped, keyed by a <c>long</c> identity.
/// </summary>
public class GamePlay : ITenantScoped
{
    public long Id { get; set; }
    public Guid BroadcasterId { get; set; }
    public Guid GameConfigId { get; set; }
    public Guid PlayerAccountId { get; set; }
    public Guid PlayerUserId { get; set; }
    public long BetAmount { get; set; }
    public GameOutcome Outcome { get; set; }
    public long PayoutAmount { get; set; }
    public long NetResult { get; set; }
    public string? ResultJson { get; set; }
    public long? BetLedgerEntryId { get; set; }
    public long? PayoutLedgerEntryId { get; set; }
    public DateTime CreatedAt { get; set; }
}
