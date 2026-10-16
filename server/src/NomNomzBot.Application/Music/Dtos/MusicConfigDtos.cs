// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;

namespace NomNomzBot.Application.Music.Dtos;

/// <summary>Music configuration for a channel.</summary>
public sealed record MusicConfigDto(
    bool IsEnabled,
    string PreferredProvider,
    int MaxQueueSize,
    int MaxRequestsPerUser,
    bool AllowYouTube,
    bool AllowSpotify,
    string MinTrustLevel,
    string? BangerPlaylistId = null,
    string? BangerPlaylistProvider = null,
    bool BangerAutoCreate = false,
    IReadOnlyDictionary<string, int>? MaxRequestsPerRole = null,
    bool RequestsOpen = true,
    int MaxSongSeconds = 360,
    bool AllowExplicit = true,
    int? PerStreamLimit = null
);

/// <summary>Request to update music configuration.</summary>
public sealed record UpdateMusicConfigDto
{
    public bool? IsEnabled { get; init; }

    /// <summary>Opens or closes new song requests; the music keeps playing and the queue stays.</summary>
    public bool? RequestsOpen { get; init; }

    [RegularExpression("^(auto|spotify|youtube)$")]
    public string? PreferredProvider { get; init; }

    [Range(1, 500)]
    public int? MaxQueueSize { get; init; }

    [Range(1, 50)]
    public int? MaxRequestsPerUser { get; init; }

    /// <summary>The longest song a viewer may request, in seconds. The broadcaster and moderators are not held to it.</summary>
    [Range(30, 3600)]
    public int? MaxSongSeconds { get; init; }

    /// <summary>When false, a song the provider marks explicit is refused.</summary>
    public bool? AllowExplicit { get; init; }

    /// <summary>
    /// The most songs one viewer may request in one stream, counting songs that already played. 0 turns the
    /// cap off (null leaves the current value alone); a value of 1 to 100 turns it on.
    /// </summary>
    [Range(0, 100)]
    public int? PerStreamLimit { get; init; }

    public bool? AllowYouTube { get; init; }
    public bool? AllowSpotify { get; init; }

    [RegularExpression("^(everyone|subscribers|vip|moderators|broadcaster)$")]
    public string? MinTrustLevel { get; init; }

    /// <summary>The playlist <c>!banger</c> adds to. Empty string clears the choice.</summary>
    [MaxLength(200)]
    public string? BangerPlaylistId { get; init; }

    [RegularExpression("^(spotify|youtube)?$")]
    public string? BangerPlaylistProvider { get; init; }

    /// <summary>With no playlist chosen, <c>!banger</c> makes one on first use.</summary>
    public bool? BangerAutoCreate { get; init; }

    /// <summary>
    /// A song request cap per role (<c>viewer|subscriber|vip|moderator|broadcaster</c>, 1 to 50). A
    /// requester gets the highest cap among the rungs they reach; a rung with no value uses
    /// <c>MaxRequestsPerUser</c>. The map is replaced whole when sent.
    /// </summary>
    [RoleRequestCaps]
    public Dictionary<string, int>? MaxRequestsPerRole { get; init; }
}
