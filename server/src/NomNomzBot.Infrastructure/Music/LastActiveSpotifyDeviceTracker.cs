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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Remembers, per channel and in memory only, the last Spotify device id observed active while reading
/// playback state. Lets a play/queue command that would otherwise fail with Spotify's
/// <c>NO_ACTIVE_DEVICE</c> transfer to that device first instead of failing outright — without ever
/// forcing a transfer away from a device the streamer is already using (nothing is remembered unless
/// Spotify itself reported it active).
///
/// "While streaming only": cleared on <see cref="ChannelOfflineEvent"/> so a device from a stream days
/// ago never gets resurrected once the channel goes live again.
/// </summary>
public interface ILastActiveSpotifyDeviceTracker
{
    void Remember(Guid broadcasterId, string deviceId);
    bool TryGet(Guid broadcasterId, out string deviceId);
    void Forget(Guid broadcasterId);
}

public sealed class LastActiveSpotifyDeviceTracker : ILastActiveSpotifyDeviceTracker
{
    private readonly ConcurrentDictionary<Guid, string> _lastActiveDeviceId = new();

    public void Remember(Guid broadcasterId, string deviceId) =>
        _lastActiveDeviceId[broadcasterId] = deviceId;

    public bool TryGet(Guid broadcasterId, out string deviceId) =>
        _lastActiveDeviceId.TryGetValue(broadcasterId, out deviceId!);

    public void Forget(Guid broadcasterId) => _lastActiveDeviceId.TryRemove(broadcasterId, out _);
}

/// <summary>Clears a channel's remembered device the moment its stream ends.</summary>
public sealed class LastActiveSpotifyDeviceStreamOfflineHandler(
    ILastActiveSpotifyDeviceTracker tracker
) : IEventHandler<ChannelOfflineEvent>
{
    public Task HandleAsync(
        ChannelOfflineEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        tracker.Forget(@event.BroadcasterId);
        return Task.CompletedTask;
    }
}
