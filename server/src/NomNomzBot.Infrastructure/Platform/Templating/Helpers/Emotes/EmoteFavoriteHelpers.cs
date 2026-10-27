// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
