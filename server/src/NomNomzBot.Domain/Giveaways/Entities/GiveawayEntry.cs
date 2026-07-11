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
/// One viewer's entry into a keyword-mode giveaway (giveaways.md G.7). Unique per
/// <c>(GiveawayId, ViewerUserId)</c> — re-entering bumps nothing; <see cref="TicketCount"/> carries the
/// sub-luck weighting (D4, 1 when unweighted). A paid entry links its <c>spend_giveaway</c> ledger debit.
/// </summary>
public class GiveawayEntry : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BroadcasterId { get; set; }

    public Guid GiveawayId { get; set; }

    /// <summary>The entrant — always a real get-or-create User row, never fabricated.</summary>
    public Guid ViewerUserId { get; set; }

    public string ViewerTwitchUserId { get; set; } = null!;

    /// <summary>Weighted tickets this entry holds in the draw pool (D4; 1 = unweighted).</summary>
    public int TicketCount { get; set; } = 1;

    /// <summary>The <c>spend_giveaway</c> ledger entry that paid for this entry, when EntryCost is set.</summary>
    public long? EntryCostLedgerEntryId { get; set; }

    public DateTime EnteredAt { get; set; }
}
