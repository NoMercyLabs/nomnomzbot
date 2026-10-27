// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Music;

/// <summary>The channel's song-request settings: open or closed, the per-viewer limit and the longest song.</summary>
internal sealed class MusicConfigHelpers()
    : MusicHelperGroup(
        MusicRow.Config(
            "music.requests.open",
            "true",
            config => PeopleText.Flag(config.IsEnabled && config.RequestsOpen)
        ),
        MusicRow.Config(
            "music.requests.maxPerUser",
            "3",
            config => StreamText.Plain(config.MaxRequestsPerUser)
        ),
        MusicRow.Config(
            "music.requests.maxDuration",
            "6:00",
            config => MusicText.Clock(config.MaxSongSeconds * 1000L)
        )
    );
