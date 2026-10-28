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
