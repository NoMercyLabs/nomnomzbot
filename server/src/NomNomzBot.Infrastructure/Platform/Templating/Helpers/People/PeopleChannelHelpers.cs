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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

/// <summary>
/// The target's own Twitch channel: the category and title it set (<c>ITwitchChannelsApi</c>) and its running
/// stream (<c>ITwitchStreamsApi</c>). An offline target is not live and has 0 viewers.
/// </summary>
internal sealed class PeopleChannelHelpers()
    : PeopleHelperGroup(
        PeopleRow.Live(
            "isLive",
            ViewerSides.Target,
            "false",
            "stream",
            PeopleRow.Map(d => d.Twitch.StreamAsync(), (s, _) => PeopleText.Flag(s is not null))
        ),
        PeopleRow.Live(
            "viewers",
            ViewerSides.Target,
            "18",
            "stream",
            PeopleRow.Map(
                d => d.Twitch.StreamAsync(),
                (s, call) => PeopleText.Count(s?.ViewerCount ?? 0, call.Culture)
            )
        ),
        PeopleRow.Live(
            "game",
            ViewerSides.Target,
            "Minecraft",
            "channel",
            PeopleRow.Map(d => d.Twitch.ChannelAsync(), (c, _) => c?.GameName ?? string.Empty)
        ),
        PeopleRow.Live(
            "title",
            ViewerSides.Target,
            "Building castles",
            "channel",
            PeopleRow.Map(d => d.Twitch.ChannelAsync(), (c, _) => c?.Title ?? string.Empty)
        )
    );
