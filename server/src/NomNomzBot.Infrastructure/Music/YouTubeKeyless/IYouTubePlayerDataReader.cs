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

/// <summary>What YouTube's public player data says about one video.</summary>
public enum YouTubePlayerDataStatus
{
    /// <summary>The video details were read; length and the live flag are trustworthy.</summary>
    Read,

    /// <summary>YouTube answered that no video exists behind the id (deleted, private or never made).</summary>
    Missing,

    /// <summary>YouTube asks the viewer to sign in to confirm their age.</summary>
    AgeRestricted,

    /// <summary>No answer could be read (network, a bot check, a changed response shape).</summary>
    Unreadable,
}

/// <summary>The player data for one video. The fields past <see cref="Status"/> are filled only when it is Read.</summary>
public sealed record YouTubePlayerData(
    YouTubePlayerDataStatus Status,
    string? Title = null,
    string? Author = null,
    int LengthSeconds = 0,
    bool IsLive = false,
    bool PlaysNowhere = false,
    string? Category = null
);

/// <summary>Reads a video's length and play flags from YouTube's public player data, with no key and no login.</summary>
public interface IYouTubePlayerDataReader
{
    Task<YouTubePlayerData> ReadAsync(string videoId, CancellationToken cancellationToken);
}
