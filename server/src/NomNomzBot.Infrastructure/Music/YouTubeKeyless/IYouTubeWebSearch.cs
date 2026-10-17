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

/// <summary>One on-demand video from YouTube's web search, in relevance order.</summary>
public sealed record YouTubeWebSearchHit(
    string VideoId,
    string Title,
    string Channel,
    int DurationMs
);

/// <summary>YouTube's public web search, with no key and no login. It never spends Data API quota.</summary>
public interface IYouTubeWebSearch
{
    /// <summary>The on-demand video hits (live streams left out); null when no answer could be read.</summary>
    Task<IReadOnlyList<YouTubeWebSearchHit>?> SearchAsync(
        string query,
        CancellationToken cancellationToken
    );
}
