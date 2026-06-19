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
    string MinTrustLevel
);

/// <summary>Request to update music configuration.</summary>
public sealed record UpdateMusicConfigDto
{
    public bool? IsEnabled { get; init; }

    [RegularExpression("^(auto|spotify|youtube)$")]
    public string? PreferredProvider { get; init; }

    [Range(1, 500)]
    public int? MaxQueueSize { get; init; }

    [Range(1, 50)]
    public int? MaxRequestsPerUser { get; init; }

    public bool? AllowYouTube { get; init; }
    public bool? AllowSpotify { get; init; }

    [RegularExpression("^(everyone|subscribers|vip|moderators|broadcaster)$")]
    public string? MinTrustLevel { get; init; }
}
