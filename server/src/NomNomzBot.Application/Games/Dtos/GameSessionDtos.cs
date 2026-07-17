// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Games.Dtos;

/// <summary>One live game session as the dashboard sees it (live-games.md §5).</summary>
public sealed record GameSessionDto(
    Guid Id,
    string GameType,
    string Status,
    int ParticipantCount,
    DateTime StartedAt,
    DateTime? JoinClosesAt,
    DateTime? ResolvedAt,
    IReadOnlyDictionary<string, object?>? State,
    IReadOnlyDictionary<string, object?>? Outcome
);

/// <summary>Session-history filter (both optional).</summary>
public sealed record GameSessionFilter(string? GameType, string? Status);

/// <summary>Start a round of <c>GameType</c> for the channel.</summary>
public sealed record StartLiveGameRequest(string GameType);

/// <summary>One catalog entry — a discovered game's manifest with wire-friendly second-based timing.</summary>
public sealed record LiveGameCatalogEntryDto(
    string GameKey,
    string DisplayName,
    IReadOnlyList<string> InputKeywords,
    string OverlayWidgetKey,
    int MinPlayers,
    int MaxPlayers,
    int LobbyWindowSeconds,
    int? TickIntervalSeconds,
    bool RequiresEntryFee
);
