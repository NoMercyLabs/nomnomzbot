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

using NomNomzBot.Domain.Identity.Enums;
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
    public Guid Id { get; set; } = MonotonicGuid.Create();

    public Guid BroadcasterId { get; set; }

    public Guid GiveawayId { get; set; }

    public Guid ViewerUserId { get; set; }

    public string ViewerTwitchUserId { get; set; } = null!;

    /// <summary>
    /// The platform the winner is on (<see cref="NomNomzBot.Domain.Identity.Enums.AuthEnums.Platform"/>
    /// string set), carried over from the winning <c>GiveawayEntry</c> — S065: lets fulfillment resolve
    /// the right DM sender instead of assuming Twitch.
    /// </summary>
    public string Provider { get; set; } = AuthEnums.Platform.Twitch;

    /// <summary>The winner's native user id on <see cref="Provider"/> (S065).</summary>
    public string? ProviderUserId { get; set; }

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
