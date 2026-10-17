// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Music.YouTubeKeyless;

/// <summary>What YouTube's keyless oEmbed endpoint says about one video.</summary>
public enum YouTubeOEmbedStatus
{
    /// <summary>The video exists and may be embedded; the title fields are filled.</summary>
    Found,

    /// <summary>oEmbed answered 401: the uploader turned embedding off.</summary>
    NotEmbeddable,

    /// <summary>No answer could be read (network, timeout, other status, bad body).</summary>
    Unreadable,
}

/// <summary>The oEmbed answer for one video; the text fields are null unless <see cref="Status"/> is Found.</summary>
public sealed record YouTubeOEmbed(
    YouTubeOEmbedStatus Status,
    string? Title = null,
    string? Author = null,
    string? ThumbnailUrl = null
);

/// <summary>Reads a video's title, channel and thumbnail from oEmbed, with no key and no login.</summary>
public interface IYouTubeOEmbedReader
{
    Task<YouTubeOEmbed> ReadAsync(string videoId, CancellationToken cancellationToken);
}
