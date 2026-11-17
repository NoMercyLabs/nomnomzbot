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

using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// The provider limits the write queue sizes itself to. Numbers marked "documented" come from the
/// provider's own live docs (checked 2026-11-17); numbers marked "ours" are planning choices, not limits.
/// Spotify publishes no numeric limit (only "a rolling 30 second window" and a 429 with Retry-After), so
/// no Spotify number is invented here: its writes go out at once and the provider adapter's own
/// Retry-After cooldown handles a 429.
/// </summary>
internal static class MusicWriteBudget
{
    /// <summary>Documented: the Data API default quota is 10,000 units per day, shared by all channels of the deployment.</summary>
    internal const int YouTubeDailyQuotaUnits = 10_000;

    /// <summary>Documented: videos.rate, playlists.insert and playlistItems.insert each cost 50 units.</summary>
    internal const int YouTubeWriteCostUnits = 50;

    /// <summary>Ours: favorites and bangers may spend at most this share of the quota; search and reads need the rest.</summary>
    internal const double YouTubeWriteQuotaShare = 0.5;

    /// <summary>Ours: how far ahead the predictor projects the recent write rate (an average stream length).</summary>
    internal static readonly TimeSpan PredictionHorizon = TimeSpan.FromHours(2);

    /// <summary>Ours: how far back the predictor looks to measure the recent write rate.</summary>
    internal static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(10);

    internal const string YouTube = "youtube";

    internal static int WriteBudgetUnits => (int)(YouTubeDailyQuotaUnits * YouTubeWriteQuotaShare);

    /// <summary>Units a write costs on a provider that has a numeric budget; zero on every other provider.</summary>
    internal static int CostOf(MusicWriteRequest request) =>
        request.Provider == YouTube ? YouTubeWriteCostUnits * request.TrackUris.Count : 0;
}
