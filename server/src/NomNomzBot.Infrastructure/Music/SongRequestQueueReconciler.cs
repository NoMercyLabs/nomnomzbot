// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Drains the song-request fair queue against what is actually playing. Every accepted request is handed
/// to the provider's own queue at admission time, so the provider decides when a request becomes the
/// current track — by a natural track end just as much as by an explicit skip. When the live playback
/// state (<see cref="PlaybackStateChangedEvent"/>, published on the poller's cadence and after every
/// mutation) reports a track we still hold as pending, that entry has stopped being pending and comes
/// out, and the sr_queue surfaces get the corrected snapshot.
/// <para>
/// Without this the fair queue would only ever grow: nothing else removes an entry once it has been
/// pushed, so the dashboard and overlay would keep listing tracks that already played.
/// </para>
/// </summary>
public sealed class SongRequestQueueReconciler : IEventHandler<PlaybackStateChangedEvent>
{
    /// <summary>Upper bound on the queue-changed snapshot — mirrors <see cref="MusicService"/>'s own.</summary>
    private const int QueueSnapshotSize = 10;

    private readonly ISongRequestQueueStore _queueStore;
    private readonly ISongRequestHandover _handover;
    private readonly ISongRequestQueuePersistence _queuePersistence;
    private readonly IEventBus _eventBus;

    public SongRequestQueueReconciler(
        ISongRequestQueueStore queueStore,
        ISongRequestHandover handover,
        ISongRequestQueuePersistence queuePersistence,
        IEventBus eventBus
    )
    {
        _queueStore = queueStore;
        _handover = handover;
        _queuePersistence = queuePersistence;
        _eventBus = eventBus;
    }

    public async Task HandleAsync(
        PlaybackStateChangedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty || string.IsNullOrEmpty(@event.TrackUri))
            return;

        string broadcasterId = @event.BroadcasterId.ToString();
        FairQueue<SongRequestEntry>? queue = _queueStore.TryGet(broadcasterId);
        if (queue is null)
            return;

        // Everything up to and including the now-playing track has stopped being pending. Dropping the
        // whole head — not just the exact match — absorbs Spotify's client-side crossfade (0–12s, set per
        // client), where one track starts while the previous is still audible and the 1s poller can see
        // the pair in either order, and a viewer skipping several tracks between ticks.
        int dropped = queue.RemoveThrough(e =>
            string.Equals(e.TrackUri, @event.TrackUri, StringComparison.OrdinalIgnoreCase)
        );

        SongRequestEntry? inFlight = _queueStore.GetInFlight(broadcasterId);
        bool inFlightIsGone =
            inFlight is not null
            && !queue.GetSnapshot().Any(e => ReferenceEquals(e.Item, inFlight));
        if (inFlightIsGone)
            _queueStore.SetInFlight(broadcasterId, null);

        // Exactly ONE request is ever at the provider. That is what keeps the fair queue authoritative:
        // everything behind the current track is still ours, so a viewer's first request can still be
        // re-ranked ahead of someone's third. Hand the next one over only once the provider is free.
        if (_queueStore.GetInFlight(broadcasterId) is null && !queue.IsEmpty)
            await _handover.HandOverNextAsync(broadcasterId, cancellationToken);

        if (dropped == 0)
            return;

        await _queuePersistence.SyncAsync(broadcasterId, queue.GetSnapshot(), cancellationToken);

        await _eventBus.PublishAsync(
            new SongRequestQueueChangedEvent
            {
                BroadcasterId = @event.BroadcasterId,
                Items =
                [
                    .. queue
                        .GetSnapshot()
                        .Take(QueueSnapshotSize)
                        .Select(e => new SongRequestQueueSnapshotItem(
                            e.Item.TrackName,
                            e.Item.RequestedBy,
                            e.Item.DurationMs / 1000
                        )),
                ],
            },
            cancellationToken
        );
    }
}
