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

/// <summary>
/// Says whether a video's channel carries YouTube's "Official Artist Channel" badge, with no key and no login.
/// </summary>
public interface IYouTubeArtistChannelReader
{
    /// <summary>False when the channel has no artist badge, and when the answer cannot be read.</summary>
    Task<bool> IsOfficialArtistAsync(string videoId, CancellationToken cancellationToken);
}
