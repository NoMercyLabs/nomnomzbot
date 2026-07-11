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

namespace NomNomzBot.Domain.Giveaways.Entities;

/// <summary>
/// One drawn winner (giveaways.md G.8) — APPEND-ONLY winner history: a re-roll marks the row
/// <c>redrawn</c> and appends a replacement, never rewrites. Carries the fulfillment trail per prize
/// mode: the assigned code, the currency payout's ledger entry, and whether the code whisper landed
/// (a failed whisper leaves the code assigned for broadcaster reveal — D6).
/// </summary>
public class GiveawayWinner : BaseEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BroadcasterId { get; set; }

    public Guid GiveawayId { get; set; }

    public Guid ViewerUserId { get; set; }

    public string ViewerTwitchUserId { get; set; } = null!;

    public DateTime DrawnAt { get; set; }

    /// <summary><c>drawn</c> | <c>claimed</c> | <c>forfeited</c> | <c>redrawn</c>.</summary>
    public string Status { get; set; } = GiveawayWinnerStatus.Drawn;

    /// <summary>True when this winner replaced a forfeited/redrawn one.</summary>
    public bool IsRedraw { get; set; }

    public Guid? AssignedCodeId { get; set; }

    /// <summary>The <c>earn_giveaway</c> ledger entry for a currency prize.</summary>
    public long? FulfillmentLedgerEntryId { get; set; }

    /// <summary>Code mode: whether the whisper delivery succeeded (null for other prize modes).</summary>
    public bool? WhisperDelivered { get; set; }
}

/// <summary>The <see cref="GiveawayWinner.Status"/> values.</summary>
public static class GiveawayWinnerStatus
{
    public const string Drawn = "drawn";
    public const string Claimed = "claimed";
    public const string Forfeited = "forfeited";
    public const string Redrawn = "redrawn";
}
