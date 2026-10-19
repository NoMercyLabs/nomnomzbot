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
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Pushes the one now-playing shape (hub event <c>music.nowPlaying</c>) on every playback state change, so a
/// widget or the dashboard reads the same fields for a Spotify track and a YouTube track.
/// </summary>
public sealed class NowPlayingBroadcastHandler : IEventHandler<PlaybackStateChangedEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IPlaybackStateReader _playbackState;

    public NowPlayingBroadcastHandler(
        IDashboardNotifier notifier,
        IPlaybackStateReader playbackState
    )
    {
        _notifier = notifier;
        _playbackState = playbackState;
    }

    public Task HandleAsync(PlaybackStateChangedEvent @event, CancellationToken ct = default) =>
        Task.CompletedTask;
}
