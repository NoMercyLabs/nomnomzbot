// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Music.Interfaces;

namespace NomNomzBot.Infrastructure.Music.YouTubeKeyless;

/// <summary>
/// Decides whether a YouTube upload is music. A song request plays music only: the upload must sit in
/// the Music category (player data says "Music", the Data API says id "10"), or come from an
/// auto-generated "- Topic" artist channel. An answer that carries no category at all is admitted:
/// YouTube always sends one today, so its absence is a response-shape change, not a verdict.
/// </summary>
public static class YouTubeMusicCategory
{
    private const string MusicName = "Music";
    private const string MusicId = "10";
    private const string TopicSuffix = "- Topic";

    private const string MusicHost = "music.youtube.com";

    /// <summary>
    /// The channel's verdict on a resolved track. A track passes when the channel admits any upload, when
    /// the track is music, or when it was asked for by a music.youtube.com link (YouTube Music only serves
    /// music, whatever category the upload carries). The verdict lives here, next to the category rule, so
    /// the providers stay config-free and every request path asks the same question.
    /// </summary>
    public static bool Admits(bool categoryOnly, TrackInfo track, string? requestedLink) =>
        !categoryOnly || track.IsMusicCategory || IsMusicLink(requestedLink);

    public static bool IsMusicLink(string? link) =>
        link is not null
        && Uri.TryCreate(link.Trim(), UriKind.Absolute, out Uri? uri)
        && string.Equals(uri.Host, MusicHost, StringComparison.OrdinalIgnoreCase);

    public static bool IsMusic(string? category, string? author) =>
        category is null
        || category == MusicName
        || category == MusicId
        || (author is not null && author.TrimEnd().EndsWith(TopicSuffix, StringComparison.Ordinal));
}
