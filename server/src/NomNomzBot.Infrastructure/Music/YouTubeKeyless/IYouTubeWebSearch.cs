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
