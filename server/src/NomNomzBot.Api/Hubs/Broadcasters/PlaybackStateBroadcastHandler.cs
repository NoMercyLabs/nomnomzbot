// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Music.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts music playback state changes to dashboard clients.</summary>
public sealed class PlaybackStateBroadcastHandler : IEventHandler<PlaybackStateChangedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public PlaybackStateBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(PlaybackStateChangedEvent @event, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(@event.BroadcasterId))
            return Task.CompletedTask;

        MusicTrackDto? track = @event.TrackName is not null
            ? new MusicTrackDto(@event.TrackName, string.Empty, string.Empty, null, 0, "unknown")
            : null;

        return _notifier.SendMusicStateAsync(
            @event.BroadcasterId,
            new(@event.IsPlaying, track),
            ct
        );
    }
}
