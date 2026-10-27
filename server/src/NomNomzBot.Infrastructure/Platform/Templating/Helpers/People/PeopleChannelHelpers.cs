// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
