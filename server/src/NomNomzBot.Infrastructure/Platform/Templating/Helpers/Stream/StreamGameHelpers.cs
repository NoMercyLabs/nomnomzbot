// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

/// <summary>The Twitch category the channel streams now: its box art, from the games API.</summary>
internal sealed class StreamGameHelpers()
    : StreamHelperGroup(
        "stream",
        StreamHelperRow.Read(
            "stream.game.boxart",
            "https://static-cdn.jtvnw.net/ttv-boxart/509670-285x380.jpg",
            StreamSources.Games,
            CurrentGameAsync,
            (game, _) => game.BoxArtUrl.Replace("{width}x{height}", BoxArtSize)
        )
    )
{
    private const string BoxArtSize = "285x380";

    private static Task<TwitchGame?> CurrentGameAsync(StreamHelperCall call) =>
        call.Channel?.CurrentGame is { Length: > 0 } game
            ? call.Data.GameAsync(game, call.Ct)
            : Task.FromResult<TwitchGame?>(null);
}
