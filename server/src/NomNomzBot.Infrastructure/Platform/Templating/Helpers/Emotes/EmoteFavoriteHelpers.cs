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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Emotes;

/// <summary>The favorite emotes the dashboard keeps for the user who owns the channel.</summary>
internal sealed class EmoteFavoriteHelpers()
    : EmoteHelperGroup(
        EmoteRow.Custom(
            "emote.user.favorite",
            "catJAM",
            EmoteSources.Favorites,
            async call => (await call.Data.FavoritesAsync()).FirstOrDefault()?.Code ?? string.Empty
        )
    );
