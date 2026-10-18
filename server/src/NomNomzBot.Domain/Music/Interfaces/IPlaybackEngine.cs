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

    Task PauseAsync(CancellationToken ct = default);

    Task ResumeAsync(CancellationToken ct = default);

    /// <summary>Pause and forget the track (used at a hand-over).</summary>
    Task StopAsync(CancellationToken ct = default);

    Task SeekAsync(int positionMs, CancellationToken ct = default);

    /// <summary>Engine volume, 0 to 100 (the music level).</summary>
    Task SetVolumeAsync(int level, CancellationToken ct = default);
}
