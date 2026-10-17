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
