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

/// <summary>Request to drop a queued song at a new zero-based position in the queue.</summary>
public sealed record MoveQueueItemDto
{
    public required int ToPosition { get; init; }
}

/// <summary>Request to remove every queued song of one requester (display name, case-insensitive).</summary>
public sealed record RemoveRequesterSongsDto
{
    public required string RequestedBy { get; init; }
}

/// <summary>How many queued songs a bulk removal took out.</summary>
public sealed record RemovedSongsDto(int RemovedCount);

/// <summary>A queue item with its zero-based position and the number viewers see
/// (<see cref="Services.QueueNumbering"/> — the one <c>!queue</c> prints and <c>!wrongsong N</c> reads back).
/// <paramref name="Cost"/> is 0 for a free request.</summary>
public sealed record QueueItemDto(
    int Position,
    int Number,
    string TrackName,
    string Artist,
    string? ImageUrl,
    int DurationMs,
    string? RequestedBy,
    int Cost = 0,
    string? RequesterId = null,
    string? RequesterColor = null
);

/// <summary>Current now-playing state. <paramref name="ShuffleState"/> and <paramref name="RepeatState"/>
/// ("off" | "track" | "context") are the live player toggles the dashboard renders — false/"off" on providers
/// that do not report them.</summary>
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
    string Provider,
    bool ShuffleState,
    string RepeatState,
    bool CanSetShuffle = true,
    bool CanSetRepeat = true,
    bool CanSkipNext = true,
    bool CanSkipPrevious = true,
    bool CanSeek = true,
    bool CanPause = true,
    bool CanResume = true,
    string? RequesterId = null,
    string? RequesterColor = null,
    string? TrackUri = null,
    string? Engine = null,
    string? ItemId = null,
    string? Title = null,
    IReadOnlyList<string>? Artists = null,
    string? AlbumArtUrl = null,
    bool Muted = false,
    string? State = null
)
{
    /// <summary>Adds the one now-playing shape (music-playback-determinism.md section 2.1) to this read.
    /// The older fields stay as they are; <see cref="Volume"/> and <see cref="IsPlaying"/> are kept as read.</summary>
    public NowPlayingDto WithShape(NowPlayingShapeDto shape) =>
        this with
        {
            Engine = shape.Engine,
            ItemId = shape.ItemId,
            Title = shape.Title,
            Artists = shape.Artists,
            AlbumArtUrl = shape.AlbumArtUrl,
            Muted = shape.Muted,
            State = shape.State,
        };
}

/// <summary>Full music queue including now playing and upcoming tracks.</summary>
public sealed record MusicQueueDto(
    NowPlayingDto? NowPlaying,
    IReadOnlyList<QueueItemDto> Queue,
    bool RequestsOpen
);
