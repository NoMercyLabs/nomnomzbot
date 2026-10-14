// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Chat.Services;

/// <summary>The most favorites one user may keep, and the providers a favorite may come from.</summary>
public static class FavoriteEmoteLimits
{
    public const int MaxPerUser = 200;

    public static readonly IReadOnlyList<string> Providers =
    [
        "Twitch",
        "SevenTv",
        "Bttv",
        "Ffz",
        "Emoji",
    ];
}

/// <summary>One favorite emote as returned to the dashboard.</summary>
public sealed record FavoriteEmoteDto(
    string Provider,
    string EmoteId,
    string Code,
    string? ImageUrl,
    DateTime CreatedAt
);

/// <summary>Body of <c>PUT /users/me/favorite-emotes</c>.</summary>
public sealed record SetFavoriteEmoteRequest(
    string Provider,
    string EmoteId,
    string Code,
    string? ImageUrl
);
