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

/// <summary>Request to add a song to the queue.</summary>
public sealed record SongRequestDto
{
    [Required, MaxLength(500)]
    public required string Query { get; init; }

    [MaxLength(50)]
    public string? RequestedBy { get; init; }
}

/// <summary>A queue item with its position.</summary>
public sealed record QueueItemDto(
    int Position,
    string TrackName,
    string Artist,
    string? ImageUrl,
    int DurationMs,
    string? RequestedBy
);

/// <summary>Current now-playing state.</summary>
public sealed record NowPlayingDto(
    string? TrackName,
    string? Artist,
    string? Album,
    string? ImageUrl,
    int DurationMs,
    int ProgressMs,
    bool IsPlaying,
    int Volume,
    string? RequestedBy,
    string Provider
);

/// <summary>Full music queue including now playing and upcoming tracks.</summary>
public sealed record MusicQueueDto(NowPlayingDto? NowPlaying, IReadOnlyList<QueueItemDto> Queue);
