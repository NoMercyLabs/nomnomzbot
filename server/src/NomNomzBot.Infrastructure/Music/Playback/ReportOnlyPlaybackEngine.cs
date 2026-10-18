// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>
/// An engine that only listens. The song request flow still hands each request to the provider itself,
/// so the director's commands change nothing here; its reports reach the director through
/// <see cref="IPlaybackReportSink"/>. It stays until the real engine adapters replace it.
/// </summary>
public sealed class ReportOnlyPlaybackEngine(EngineKind kind) : IPlaybackEngine
{
    public EngineKind Kind { get; } = kind;

    public event Func<EngineSignal, Task> Reports
    {
        add { }
        remove { }
    }

    public Task<EngineStartResult> StartAsync(PlaybackItem item, CancellationToken ct = default) =>
        Task.FromResult(EngineStartResult.Ok);

    public Task PauseAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task ResumeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task SeekAsync(int positionMs, CancellationToken ct = default) => Task.CompletedTask;

    public Task SetVolumeAsync(int level, CancellationToken ct = default) => Task.CompletedTask;
}
