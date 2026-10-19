// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
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

    public Task HandleAsync(PlaybackStateChangedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        PlaybackState state = _playbackState.StateOf(@event.BroadcasterId);
        PlaybackLevel level = _playbackState.LevelOf(@event.BroadcasterId);
        DirectorNowPlaying? shape = DirectorNowPlaying.Build(
            state,
            level,
            @event.Provider,
            @event.TrackUri,
            @event.TrackName,
            @event.Artist,
            @event.AlbumArtUrl,
            @event.DurationMs,
            @event.ProgressMs,
            @event.IsPlaying,
            @event.RequestedBy
        );
        NowPlayingShapeDto data = shape is null
            ? NowPlayingShapeDto.Empty(state.Kind, level)
            : NowPlayingShapeDto.From(shape);

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            NowPlayingShapeDto.EventName,
            data,
            ct
        );
    }
}
