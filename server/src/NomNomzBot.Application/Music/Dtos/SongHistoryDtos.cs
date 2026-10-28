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
    DateTime RequestedAt,
    string? RequesterColor = null
);

/// <summary>A viewer and how many songs they have requested in this channel, all time.</summary>
public sealed record TopRequesterDto(
    string UserId,
    string DisplayName,
    int Count,
    string? Color = null
);

/// <summary>The Twitch id and chat color (#RRGGBB, null until the viewer was seen in chat) of a person named by display name.</summary>
public sealed record RequesterIdentityDto(string UserId, string? Color);
