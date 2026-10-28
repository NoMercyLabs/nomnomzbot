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
