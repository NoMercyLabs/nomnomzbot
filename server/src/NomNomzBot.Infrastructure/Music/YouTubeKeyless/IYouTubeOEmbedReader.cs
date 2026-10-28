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
