// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.MediaShare.Dtos;

/// <summary>The playback commands the queue owner accepts, as the wire text.</summary>
public static class MediaPlaybackCommands
{
    public const string Play = "play";
    public const string Pause = "pause";
    public const string Stop = "stop";
    public const string Skip = "skip";
    public const string Seek = "seek";
    public const string Volume = "volume";
    public const string Replay = "replay";
    public const string PlayNow = "play_now";

    public static readonly IReadOnlyList<string> All =
    [
        Play,
        Pause,
        Stop,
        Skip,
        Seek,
        Volume,
        Replay,
        PlayNow,
    ];
}

/// <summary>The states a media player reports, as the wire text.</summary>
public static class MediaPlayerStates
{
    public const string Idle = "idle";
    public const string Playing = "playing";
    public const string Paused = "paused";
    public const string Ended = "ended";
    public const string Error = "error";
}

/// <summary>
/// One playback command. <paramref name="PositionSeconds"/> is for seek, <paramref name="Volume"/> (0..100)
/// for volume, <paramref name="RequestId"/> for play_now and replay of a finished item.
/// </summary>
public sealed record MediaPlaybackCommandRequest(
    string Command,
    int? PositionSeconds = null,
    int? Volume = null,
    Guid? RequestId = null
);

/// <summary>What the overlay player says it is doing.</summary>
public sealed record MediaPlayerStateReport(
    Guid? RequestId,
    string State,
    int PositionSeconds,
    int DurationSeconds,
    int Volume
);

/// <summary>What the player is doing now, with the item it plays.</summary>
public sealed record MediaPlayerStateDto(
    string State,
    Guid? RequestId,
    string? Title,
    string? SharerName,
    string? ThumbnailUrl,
    int PositionSeconds,
    int DurationSeconds,
    int Volume
)
{
    public static MediaPlayerStateDto Idle { get; } =
        new(MediaPlayerStates.Idle, null, null, null, null, 0, 0, 100);
}
