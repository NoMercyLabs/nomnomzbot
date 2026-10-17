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
