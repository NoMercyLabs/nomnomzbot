// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Infrastructure.Commands.Builtins;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Music;

/// <summary>The track that plays now: title, artist, album, cover, link and who asked for it.</summary>
internal sealed class MusicNowHelpers()
    : MusicHelperGroup(
        MusicRow.Now(
            "music.now",
            "Song A by Artist A",
            (now, _) => MusicText.Track(now.TrackName, now.Artist)
        ),
        MusicRow.Now("music.now.title", "Song A", (now, _) => Filled(now.TrackName)),
        MusicRow.Now("music.now.artist", "Artist A", (now, _) => Filled(now.Artist)),
        MusicRow.Now("music.now.album", "Album A", (now, _) => Filled(now.Album)),
        MusicRow.Now(
            "music.now.cover",
            "https://i.scdn.co/image/sample",
            (now, _) => Filled(now.ImageUrl)
        ),
        MusicRow.Now(
            "music.now.link",
            "https://open.spotify.com/track/sample",
            (now, _) => Filled(TrackLinks.ToWebLink(now.TrackUri))
        ),
        MusicRow.Now("music.now.requester", "ViewerTwo", (now, _) => Filled(now.RequestedBy)),
        MusicRow.Now(
            "music.now.requester.mention",
            "@ViewerTwo",
            (now, _) => Filled(now.RequestedBy) is { } who ? "@" + who : null
        )
    )
{
    private static string? Filled(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
