// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
