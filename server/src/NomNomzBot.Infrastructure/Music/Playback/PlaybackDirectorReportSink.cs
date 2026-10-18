// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>
/// Gives each channel one <see cref="PlaybackDirector"/> over its live song request queue and passes
/// engine reports to it. The director is the only thing that removes a row from that queue.
/// </summary>
public sealed class PlaybackDirectorReportSink(
    ISongRequestQueueStore queueStore,
    IEventBus bus,
    TimeProvider time
) : IPlaybackReportSink, IDisposable
{
    private readonly ConcurrentDictionary<Guid, PlaybackDirector> _directors = new();

    public Task ReportAsync(
        Guid broadcasterId,
        EngineSignal signal,
        CancellationToken ct = default
    ) => _directors.GetOrAdd(broadcasterId, CreateDirector).HandleAsync(signal, ct);

    public void Dispose()
    {
        foreach (PlaybackDirector director in _directors.Values)
            director.Dispose();
    }

    private PlaybackDirector CreateDirector(Guid broadcasterId) =>
        new(
            broadcasterId,
            [
                new ReportOnlyPlaybackEngine(EngineKind.Spotify),
                new ReportOnlyPlaybackEngine(EngineKind.YouTube),
            ],
            new SongRequestPlaybackQueue(queueStore.GetOrCreate(broadcasterId.ToString())),
            time,
            bus
        );
}
