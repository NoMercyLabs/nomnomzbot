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

namespace NomNomzBot.Application.Music.Dtos;

/// <summary>A channel's blocked song-request track (music-sr.md — the legacy <c>!bansong</c> list).</summary>
public sealed record BlockedTrackDto(
    Guid Id,
    string Provider,
    string TrackUri,
    string Title,
    string? Reason,
    string? BlockedByUserId,
    DateTime CreatedAt
);

/// <summary>Request body for blocking a track from song requests.</summary>
public sealed record BlockTrackRequest(
    string Provider,
    string TrackUri,
    string Title,
    string? Reason = null,
    string? BlockedByUserId = null
);
