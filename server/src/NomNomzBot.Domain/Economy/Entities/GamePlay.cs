// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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

    // Live games (live-games.md D10): set for every live-game award row; null for instant PlayAsync games.
    public Guid? GameSessionId { get; set; }
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
