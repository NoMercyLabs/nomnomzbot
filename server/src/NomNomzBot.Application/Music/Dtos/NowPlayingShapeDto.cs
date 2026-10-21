// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Application.Music.Dtos;

/// <summary>
/// The wire form of the one now-playing shape (music-playback-determinism.md section 2.1), pushed as the
/// hub event <c>music.nowPlaying</c>. <paramref name="Engine"/> is "spotify" or "youtube"; every other field
/// is filled the same way for both engines. <paramref name="State"/> is the camelCase director state:
/// idle, starting, playing, pausedByOwner or blocked.
/// </summary>
public sealed record NowPlayingShapeDto(
    string Engine,
    string? ItemId,
    string Title,
    IReadOnlyList<string> Artists,
    string? AlbumArtUrl,
    int DurationMs,
    int ProgressMs,
    bool IsPlaying,
    string? RequestedBy,
    int Volume,
    bool Muted,
    string State,
    string? BlockedReason = null
)
{
    /// <summary>The hub event name this shape is pushed under.</summary>
    public const string EventName = "music.nowPlaying";

    /// <summary>Maps the domain shape to its wire form.</summary>
    public static NowPlayingShapeDto From(DirectorNowPlaying shape) =>
        new(
            shape.Engine == EngineKind.YouTube ? "youtube" : "spotify",
            shape.ItemId,
            shape.Title,
            shape.Artists,
            shape.AlbumArtUrl,
            shape.DurationMs,
            shape.ProgressMs,
            shape.IsPlaying,
            shape.RequestedBy,
            shape.Volume,
            shape.Muted,
            StateName(shape.State)
        );

    /// <summary>Builds the wire shape from the raw playback fields: the loaded card when a track is known,
    /// otherwise the empty card with the director state and level. One path for the hub push, the widget
    /// push and the overlay seed read, so they never drift.</summary>
    public static NowPlayingShapeDto Build(
        PlaybackState state,
        PlaybackLevel level,
        string? provider,
        string? trackUri,
        string? title,
        string? artist,
        string? albumArtUrl,
        int durationMs,
        int progressMs,
        bool isPlaying,
        string? requestedBy
    )
    {
        DirectorNowPlaying? shape = DirectorNowPlaying.Build(
            state,
            level,
            provider,
            trackUri,
            title,
            artist,
            albumArtUrl,
            durationMs,
            progressMs,
            isPlaying,
            requestedBy
        );

        return shape is null ? Empty(state.Kind, level) : From(shape);
    }

    /// <summary>The card pushed when nothing is loaded: no title, no artists, the director state and level.
    /// The engine is "spotify" because the field is required; clients read the state and the empty title.</summary>
    public static NowPlayingShapeDto Empty(PlaybackStateKind state, PlaybackLevel level) =>
        new(
            "spotify",
            null,
            string.Empty,
            [],
            null,
            0,
            0,
            false,
            null,
            level.Volume,
            level.Muted,
            StateName(state)
        );

    private static string StateName(PlaybackStateKind state) =>
        state switch
        {
            PlaybackStateKind.Idle => "idle",
            PlaybackStateKind.Starting => "starting",
            PlaybackStateKind.Playing => "playing",
            PlaybackStateKind.PausedByOwner => "pausedByOwner",
            _ => "blocked",
        };
}
