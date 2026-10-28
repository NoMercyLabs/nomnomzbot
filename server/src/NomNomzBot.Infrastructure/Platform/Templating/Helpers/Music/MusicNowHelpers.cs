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
