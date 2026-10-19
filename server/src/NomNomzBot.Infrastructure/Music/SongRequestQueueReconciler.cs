// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Music.Playback;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Turns the live playback state (<see cref="PlaybackStateChangedEvent"/>, published on the poller's
/// cadence and after every mutation) into engine reports for the playback director, and keeps the
/// in-flight marker and the sr_queue surfaces in step with what the director did. It
/// never removes a queue row itself: a row leaves only when the director sees it play, or on an
/// explicit remove.
/// </summary>
public sealed class SongRequestQueueReconciler : IEventHandler<PlaybackStateChangedEvent>
{
    /// <summary>Upper bound on the queue-changed snapshot — mirrors <see cref="MusicService"/>'s own.</summary>
    private const int QueueSnapshotSize = 10;

    private readonly ISongRequestQueueStore _queueStore;
    private readonly ISongRequestQueuePersistence _queuePersistence;
    private readonly IEventBus _eventBus;
    private readonly IPlaybackReportSink _sink;

    public SongRequestQueueReconciler(
        ISongRequestQueueStore queueStore,
        ISongRequestQueuePersistence queuePersistence,
        IEventBus eventBus,
        IPlaybackReportSink sink
    )
    {
        _sink = sink;
        _queueStore = queueStore;
        _queuePersistence = queuePersistence;
        _eventBus = eventBus;
    }

    /// <summary>
    /// The in-flight request is dropped from the store as soon as its track plays, so the requester is kept
    /// aside until a different track starts. A track nobody requested clears it, which is the "not
    /// requested" state (the UI then shows the provider instead).
    /// </summary>
    private void RememberRequesterOfPlayingTrack(string broadcasterId, string playingTrackUri)
    {
        SongRequestEntry? inFlight = _queueStore.GetInFlight(broadcasterId);
        if (
            inFlight is not null
            && string.Equals(inFlight.TrackUri, playingTrackUri, StringComparison.Ordinal)
        )
        {
            _queueStore.SetPlayingRequest(broadcasterId, inFlight);
            return;
        }

        SongRequestEntry? remembered = _queueStore.GetPlayingRequest(broadcasterId);
        if (
            remembered is not null
            && !string.Equals(remembered.TrackUri, playingTrackUri, StringComparison.Ordinal)
        )
            _queueStore.SetPlayingRequest(broadcasterId, null);
    }

    /// <summary>The engine signal for what the player reported: a track end, or a playing/paused report.</summary>
    private static EngineSignal ToSignal(PlaybackStateChangedEvent @event, string trackUri)
    {
        EngineKind engine = EngineKinds.ForTrack(@event.Provider, trackUri);
        return @event.TrackEnded
            ? new TrackEnded(engine, trackUri)
            : new EngineReport(
                engine,
                trackUri,
                @event.IsPlaying,
                @event.ProgressMs,
                @event.DurationMs
            );
    }

    public async Task HandleAsync(
        PlaybackStateChangedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty || string.IsNullOrEmpty(@event.TrackUri))
            return;

        string broadcasterId = @event.BroadcasterId.ToString();
        RememberRequesterOfPlayingTrack(broadcasterId, @event.TrackUri);
        FairQueue<SongRequestEntry>? queue = _queueStore.TryGet(broadcasterId);
        if (queue is null)
            return;

        // This class only reports. The playback director sees what plays and is the only thing that
        // removes a row: the played head, one queued row that played out of order, or nothing.
        // An engine-fed event (the Spotify poll) was already given to its engine, which reports it to the
        // director; reporting it here too would feed the director a second track end. The director has
        // already acted by now, so the rows it removed cannot be counted here: such an event always syncs.
        int before = queue.Count;
        if (!@event.FedToEngine && !@event.ReportWithheld)
            await _sink.ReportAsync(
                @event.BroadcasterId,
                ToSignal(@event, @event.TrackUri),
                cancellationToken
            );
        bool queueMayHaveChanged = @event.FedToEngine || before != queue.Count;

        SongRequestEntry? inFlight = _queueStore.GetInFlight(broadcasterId);
        bool inFlightIsGone =
            inFlight is not null
            && !queue.GetSnapshot().Any(e => ReferenceEquals(e.Item, inFlight));
        if (inFlightIsGone)
            _queueStore.SetInFlight(broadcasterId, null);

        if (!queueMayHaveChanged)
            return;

        await _queuePersistence.SyncAsync(
            broadcasterId,
            queue.GetSnapshot(),
            cancellationToken,
            _queueStore.GetInFlight(broadcasterId)
        );

        await _eventBus.PublishAsync(
            new SongRequestQueueChangedEvent
            {
                BroadcasterId = @event.BroadcasterId,
                Items =
                [
                    .. queue
                        .GetSnapshot()
                        .Take(QueueSnapshotSize)
                        .Select(
                            (e, index) =>
                                new SongRequestQueueSnapshotItem(
                                    e.Item.TrackName,
                                    e.Item.RequestedBy,
                                    e.Item.DurationMs / 1000,
                                    QueueNumbering.NumberOf(index),
                                    e.Item.Code
                                )
                        ),
                ],
            },
            cancellationToken
        );
    }
}
