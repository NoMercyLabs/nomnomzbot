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
/// Gives each channel one <see cref="PlaybackDirector"/> over its live song request queue and the real
/// engines of <see cref="IPlaybackEngineFactory"/>, and passes every input to it. The director is the only
/// thing that removes a row from that queue or sends an engine command.
/// </summary>
public sealed class PlaybackDirectorRegistry(
    ISongRequestQueueStore queueStore,
    IPlaybackEngineFactory engines,
    IEventBus bus,
    TimeProvider time
) : IPlaybackInputSink, IPlaybackReportSink, IDisposable
{
    private readonly ConcurrentDictionary<Guid, Lazy<PlaybackDirector>> _directors = new();

    public Task SendAsync(
        Guid broadcasterId,
        PlaybackInput input,
        CancellationToken ct = default
    ) => DirectorOf(broadcasterId).HandleAsync(input, ct);

    public Task ReportAsync(
        Guid broadcasterId,
        EngineSignal signal,
        CancellationToken ct = default
    ) => SendAsync(broadcasterId, signal, ct);

    public void Dispose()
    {
        foreach (Lazy<PlaybackDirector> director in _directors.Values)
            if (director.IsValueCreated)
                director.Value.Dispose();
    }

    private PlaybackDirector DirectorOf(Guid broadcasterId) =>
        _directors.GetOrAdd(broadcasterId, id => new(() => CreateDirector(id))).Value;

    private PlaybackDirector CreateDirector(Guid broadcasterId) =>
        new(
            broadcasterId,
            engines.Create(broadcasterId),
            new SongRequestPlaybackQueue(queueStore.GetOrCreate(broadcasterId.ToString())),
            time,
            bus
        );
}
