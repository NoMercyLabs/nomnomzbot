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

/// <summary>
/// One accepted song request in the channel's history. <paramref name="TrackName"/> is empty for a row that
/// was rebuilt from the journal, which carries only the uri.
/// </summary>
public sealed record SongHistoryItemDto(
    string TrackName,
    string Artist,
    string? ImageUrl,
    string Provider,
    string RequesterId,
    string RequesterName,
    DateTime RequestedAt
);

/// <summary>A viewer and how many songs they have requested in this channel, all time.</summary>
public sealed record TopRequesterDto(string UserId, string DisplayName, int Count);
