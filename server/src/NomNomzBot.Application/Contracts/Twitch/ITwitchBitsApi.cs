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

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// The Helix "Bits" category sub-client: the Bits leaderboard, cheermotes, and custom power-ups
/// (twitch-helix.md §3.2). One of the grouped sub-clients exposed by <see cref="ITwitchHelixClient"/>.
/// Every tenant-scoped method takes the owning tenant as a <see cref="Guid"/> and resolves it to the Twitch id
/// internally (the invariant: a Guid never reaches Twitch). Each returns <see cref="Result"/>/<see cref="Result{T}"/>
/// carrying a closed <see cref="TwitchErrorCodes"/> on failure.
/// </summary>
public interface ITwitchBitsApi
{
    /// <summary>
    /// Get Bits Leaderboard — the ranked bits leaders for the authenticated broadcaster. The broadcaster
    /// context comes from the user token, so there is no <c>broadcaster_id</c> query param. <paramref name="count"/>
    /// caps the rows (1–100, default 10), <paramref name="period"/> is one of <c>day|week|month|year|all</c>,
    /// <paramref name="startedAt"/> anchors the period window, and <paramref name="userId"/> centres the board on
    /// one viewer. Requires <c>bits:read</c>. NOTE: the generic list envelope surfaces only <c>data[]</c> — the
    /// response's <c>date_range</c> and <c>total</c> are not exposed by this method (transport gap).
    /// </summary>
    Task<Result<IReadOnlyList<TwitchBitsLeaderboardEntry>>> GetBitsLeaderboardAsync(
        Guid broadcasterId,
        int? count,
        string? period,
        DateTimeOffset? startedAt,
        string? userId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Get Cheermotes — the cheermotes usable in chat. App token (or user token); no scope. When
    /// <paramref name="broadcasterId"/> is supplied it is resolved to the channel id and sent as
    /// <c>broadcaster_id</c> to include that channel's custom cheermotes; when null only the global set is returned.
    /// </summary>
    Task<Result<IReadOnlyList<TwitchCheermote>>> GetCheermotesAsync(
        Guid? broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Get Custom Power-up — the channel's Bits custom power-ups, optionally narrowed to specific
    /// <paramref name="ids"/> (raw Twitch power-up ids, max 50). Requires <c>bits:read</c>.
    /// </summary>
    Task<Result<IReadOnlyList<TwitchCustomPowerUp>>> GetCustomPowerUpsAsync(
        Guid broadcasterId,
        IReadOnlyList<string>? ids,
        CancellationToken ct = default
    );
}
