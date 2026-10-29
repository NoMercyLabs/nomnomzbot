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
/// One viewer's entry into a keyword-mode giveaway (giveaways.md G.7). Unique per
/// <c>(GiveawayId, ViewerUserId)</c> — one row per viewer; every extra entry (up to the giveaway's
/// <c>MaxEntriesPerUser</c>) bumps <see cref="EntryCount"/> and adds tickets. <see cref="TicketCount"/>
/// carries the sub-luck weighting (D4, 1 when unweighted). A paid entry links its <c>spend_giveaway</c> ledger debit.
/// </summary>
public class GiveawayEntry : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = MonotonicGuid.Create();

    public Guid BroadcasterId { get; set; }

    public Guid GiveawayId { get; set; }

    /// <summary>The entrant — always a real get-or-create User row, never fabricated.</summary>
    public Guid ViewerUserId { get; set; }

    public string ViewerTwitchUserId { get; set; } = null!;

    /// <summary>
    /// The platform the entrant is on (<see cref="NomNomzBot.Domain.Identity.Enums.AuthEnums.Platform"/>
    /// string set) — S065: lets fulfillment resolve the right DM sender instead of assuming Twitch.
    /// </summary>
    public string Provider { get; set; } = AuthEnums.Platform.Twitch;

    /// <summary>The entrant's native user id on <see cref="Provider"/> (S065).</summary>
    public string? ProviderUserId { get; set; }

    /// <summary>How many entries this viewer holds; never above the giveaway's <c>MaxEntriesPerUser</c>.</summary>
    public int EntryCount { get; set; } = 1;

    /// <summary>Weighted tickets this viewer holds in the draw pool, summed over all entries (D4; 1 = unweighted).</summary>
    public int TicketCount { get; set; } = 1;

    /// <summary>The <c>spend_giveaway</c> ledger entry that paid for this entry, when EntryCost is set.</summary>
    public long? EntryCostLedgerEntryId { get; set; }

    public DateTime EnteredAt { get; set; }
}
