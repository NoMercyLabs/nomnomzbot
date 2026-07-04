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

namespace NomNomzBot.Application.Contracts.Music;

/// <summary>A playlist on the broadcaster's own provider account (music-sr.md §4, manage surface).</summary>
public sealed record MusicPlaylistDto(
    string Id,
    string Name,
    string? Description,
    bool IsPublic,
    int TrackCount,
    string? ImageUrl,
    string Provider
);

public sealed record CreateMusicPlaylistDto
{
    [Required, MaxLength(150)]
    public required string Name { get; init; }

    [MaxLength(300)]
    public string? Description { get; init; }

    public bool IsPublic { get; init; }
}

public sealed record UpdateMusicPlaylistDto
{
    [MaxLength(150)]
    public string? Name { get; init; }

    [MaxLength(300)]
    public string? Description { get; init; }

    public bool? IsPublic { get; init; }
}

public enum MusicRating
{
    None,
    Like,
    Dislike,
}

/// <summary>What a follow/unfollow targets — decides the gating capability (channel → Subscriptions, artist/playlist → Library).</summary>
public enum MusicFollowTarget
{
    Channel,
    Artist,
    Playlist,
}
