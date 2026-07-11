// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Giveaways.Dtos;

namespace NomNomzBot.Application.Giveaways.Services;

/// <summary>
/// The giveaway campaign lifecycle (giveaways.md §3.1): CRUD, open/close (one active giveaway per
/// channel — D2), viewer entry (eligibility + dedupe + entry cost + sub-luck tickets), the weighted
/// CSPRNG draw with per-prize-mode fulfillment, and append-only winner history with re-roll.
/// </summary>
public interface IGiveawayService
{
    Task<Result<GiveawayDto>> CreateAsync(
        Guid broadcasterId,
        UpsertGiveawayRequest request,
        CancellationToken ct = default
    );

    /// <summary>Draft/closed only — an open or drawn giveaway's config is frozen.</summary>
    Task<Result<GiveawayDto>> UpdateAsync(
        Guid broadcasterId,
        Guid giveawayId,
        UpsertGiveawayRequest request,
        CancellationToken ct = default
    );

    Task<Result> DeleteAsync(Guid broadcasterId, Guid giveawayId, CancellationToken ct = default);

    Task<Result<PagedList<GiveawayDto>>> ListAsync(
        Guid broadcasterId,
        GiveawayFilter filter,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    Task<Result<GiveawayDto>> GetAsync(
        Guid broadcasterId,
        Guid giveawayId,
        CancellationToken ct = default
    );

    /// <summary>Opens for entries — fails <c>GIVEAWAY_ALREADY_ACTIVE</c> when another giveaway is open or
    /// closed-but-undrawn (D2); publishes <c>GiveawayOpenedEvent</c> (the keyword listener arms off it).</summary>
    Task<Result<GiveawayDto>> OpenAsync(
        Guid broadcasterId,
        Guid giveawayId,
        CancellationToken ct = default
    );

    /// <summary>Stops accepting entries; the giveaway stays drawable.</summary>
    Task<Result<GiveawayDto>> CloseAsync(
        Guid broadcasterId,
        Guid giveawayId,
        CancellationToken ct = default
    );

    /// <summary>A viewer joins (keyword listener or the <c>enter_giveaway</c> action): eligibility (D3),
    /// unique-entry dedupe + <c>MaxEntriesPerUser</c>, the <c>spend_giveaway</c> entry-cost debit
    /// (<c>INSUFFICIENT_FUNDS</c> when broke), and the weighted <c>TicketCount</c> (D4).</summary>
    Task<Result<GiveawayEntryDto>> EnterAsync(
        Guid broadcasterId,
        Guid giveawayId,
        Guid viewerUserId,
        CancellationToken ct = default
    );

    /// <summary>Draws <c>WinnerCount</c> DISTINCT winners with a CSPRNG over the ticket-weighted pool
    /// (entries, or the eligible active-viewer set), never the broadcaster (mods iff excluded), fulfills
    /// per <c>PrizeMode</c> (§4), appends winner rows, and publishes <c>GiveawayDrawnEvent</c> — one
    /// <c>IUnitOfWork</c> transaction.</summary>
    Task<Result<IReadOnlyList<GiveawayWinnerDto>>> DrawAsync(
        Guid broadcasterId,
        Guid giveawayId,
        CancellationToken ct = default
    );

    /// <summary>Replaces one winner: marks it <c>redrawn</c>, draws a replacement excluding ALL prior
    /// winners, and re-runs fulfillment for the replacement.</summary>
    Task<Result<GiveawayWinnerDto>> RedrawAsync(
        Guid broadcasterId,
        Guid giveawayId,
        Guid winnerId,
        CancellationToken ct = default
    );

    Task<Result<PagedList<GiveawayWinnerDto>>> GetWinnersAsync(
        Guid broadcasterId,
        Guid giveawayId,
        PaginationParams pagination,
        CancellationToken ct = default
    );
}
