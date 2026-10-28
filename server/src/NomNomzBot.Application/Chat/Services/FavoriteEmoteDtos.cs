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
