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

/// <summary>
/// A follow relationship on the broadcaster's own provider account (music-sr.md §3.10): a followed
/// artist or playlist (Spotify) or a subscribed channel (YouTube). <paramref name="TargetId"/> is the
/// provider-native id (Spotify artist/playlist id, YouTube channel id).
/// </summary>
public sealed record MusicFollowDto(string TargetId, string Name, string? ImageUrl);
