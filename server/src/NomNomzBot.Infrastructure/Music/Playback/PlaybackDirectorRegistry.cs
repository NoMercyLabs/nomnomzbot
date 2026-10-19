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
/// thing that removes a row from that queue or sends an engine command. The registry keeps the engines it
/// built, so a poll result can be given to the engine that learns by polling.
/// </summary>
public sealed class PlaybackDirectorRegistry(
    ISongRequestQueueStore queueStore,
    IPlaybackEngineFactory engines,
    IEventBus bus,
    TimeProvider time
) : IPlaybackInputSink, IPlaybackStateReader, IPlaybackReportSink, IEngineFeed, IDisposable
{
    private readonly ConcurrentDictionary<Guid, Lazy<ChannelPlayback>> _channels = new();

    public Task SendAsync(
        Guid broadcasterId,
        PlaybackInput input,
        CancellationToken ct = default
    ) => ChannelOf(broadcasterId).Director.HandleAsync(input, ct);

    public PlaybackState StateOf(Guid channelId) =>
        _channels.TryGetValue(channelId, out Lazy<ChannelPlayback>? channel)
            ? channel.Value.Director.State
            : PlaybackState.Idle;

    public PlaybackLevel LevelOf(Guid channelId) =>
        _channels.TryGetValue(channelId, out Lazy<ChannelPlayback>? channel)
            ? channel.Value.Director.Level
            : PlaybackLevel.Default;

    public Task ReportAsync(
        Guid broadcasterId,
        EngineSignal signal,
        CancellationToken ct = default
    ) => SendAsync(broadcasterId, signal, ct);

    /// <summary>
    /// Gives the poll to the channel's polled engine. A channel that has no director yet, or has no polled
    /// engine, is left alone: a poll alone never creates playback state.
    /// </summary>
    public async Task ObservePollAsync(
        Guid broadcasterId,
        TrackInfo? polled,
        CancellationToken ct = default
    )
    {
        if (!_channels.TryGetValue(broadcasterId, out Lazy<ChannelPlayback>? channel))
            return;

        foreach (
            IPolledPlaybackEngine engine in channel.Value.Engines.OfType<IPolledPlaybackEngine>()
        )
            await engine.ObserveAsync(polled);
    }

    /// <summary>
    /// Gives the player report to the channel's player-reporting engine. A channel that has no director yet,
    /// or has no such engine, is left alone: a report alone never creates playback state.
    /// </summary>
    public async Task<bool> ReportPlayerAsync(
        Guid broadcasterId,
        YouTubePlayerReport report,
        CancellationToken ct = default
    )
    {
        if (!_channels.TryGetValue(broadcasterId, out Lazy<ChannelPlayback>? channel))
            return false;

        bool taken = false;
        foreach (
            IPlayerReportingEngine engine in channel.Value.Engines.OfType<IPlayerReportingEngine>()
        )
        {
            await engine.ReportAsync(report);
            taken = true;
        }

        return taken;
    }

    public void Dispose()
    {
        foreach (Lazy<ChannelPlayback> channel in _channels.Values)
            if (channel.IsValueCreated)
                channel.Value.Director.Dispose();
    }

    private ChannelPlayback ChannelOf(Guid broadcasterId) =>
        _channels.GetOrAdd(broadcasterId, id => new(() => Create(id))).Value;

    private ChannelPlayback Create(Guid broadcasterId)
    {
        IReadOnlyList<IPlaybackEngine> built = engines.Create(broadcasterId);

        return new(
            new PlaybackDirector(
                broadcasterId,
                built,
                new SongRequestPlaybackQueue(queueStore.GetOrCreate(broadcasterId.ToString())),
                time,
                bus
            ),
            built
        );
    }

    private sealed record ChannelPlayback(
        PlaybackDirector Director,
        IReadOnlyList<IPlaybackEngine> Engines
    );
}
