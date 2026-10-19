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

namespace NomNomzBot.Domain.Music.Interfaces;

/// <summary>
/// One playback engine. Spotify and YouTube each implement all of it (music-playback-determinism.md
/// section 2.1). Only the playback director calls these members.
/// </summary>
public interface IPlaybackEngine
{
    /// <summary>Data only; no caller branches on it.</summary>
    EngineKind Kind { get; }

    /// <summary>Engine reports and track ends, in one shape for both engines.</summary>
    event Func<EngineSignal, Task> Reports;

    Task<EngineStartResult> StartAsync(PlaybackItem item, CancellationToken ct = default);

    /// <summary>
    /// Lines the item up to play when the owner's current track on this engine ends, without interrupting
    /// it. An engine with no queue of its own starts the item instead.
    /// </summary>
    Task<EngineStartResult> QueueNextAsync(PlaybackItem item, CancellationToken ct = default) =>
        StartAsync(item, ct);

    Task PauseAsync(CancellationToken ct = default);

    Task ResumeAsync(CancellationToken ct = default);

    /// <summary>Pause and forget the track (used at a hand-over).</summary>
    Task StopAsync(CancellationToken ct = default);

    Task SeekAsync(int positionMs, CancellationToken ct = default);

    /// <summary>Engine volume, 0 to 100 (the music level).</summary>
    Task SetVolumeAsync(int level, CancellationToken ct = default);

    /// <summary>
    /// The music widget page came online while this engine holds the track. An engine whose player lives on
    /// the page brings the track back to the new page; <paramref name="pageIsPlaying"/> is true when the page
    /// already plays it. Other engines have nothing to do.
    /// </summary>
    Task ReattachOutputAsync(bool pageIsPlaying, CancellationToken ct = default) =>
        Task.CompletedTask;

    /// <summary>
    /// One read of what the engine's player plays right now, for the director that has no report from this
    /// engine yet. The answer comes back to the caller and is not raised as a signal, because the caller
    /// holds the director's turn. Null means nothing plays or the engine cannot tell. An engine that has no
    /// player to read keeps this default.
    /// </summary>
    Task<EngineReport?> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult<EngineReport?>(null);
}
