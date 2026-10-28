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

using NomNomzBot.Domain.Music.Interfaces;

namespace NomNomzBot.Infrastructure.Music.YouTubeKeyless;

/// <summary>
/// Turns one video id into a song-request track with no key and no login. <see cref="MusicProviderFailureReason.Unavailable"/>
/// and <see cref="MusicProviderFailureReason.UnknownLength"/> mean the keyless data could not decide; every
/// other answer is a verdict on the video itself.
/// </summary>
public interface IYouTubeKeylessResolver
{
    Task<(TrackInfo? Track, MusicProviderFailureReason Failure)> ResolveAsync(
        string videoId,
        CancellationToken cancellationToken
    );
}
