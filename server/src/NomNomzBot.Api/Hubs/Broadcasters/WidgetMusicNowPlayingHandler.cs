// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Playback change → the widgets that subscribe to <c>music.nowPlaying</c>, as the one now-playing shape
/// (music-playback-determinism.md section 2.1): the same fields for a Spotify and a YouTube track, only
/// <c>engine</c> differs. A standing display, so it is never captured for replay.
/// </summary>
public sealed class WidgetMusicNowPlayingHandler(
    IApplicationDbContext db,
    IWidgetNotifier notifier,
    IPlaybackStateReader playbackState
) : IEventHandler<PlaybackStateChangedEvent>
{
    public Task HandleAsync(
        PlaybackStateChangedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        NowPlayingShapeDto shape = NowPlayingShapeDto.Build(
            playbackState.StateOf(@event.BroadcasterId),
            playbackState.LevelOf(@event.BroadcasterId),
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

        return WidgetAlertDispatch.RouteAsync(
            db,
            notifier,
            @event.BroadcasterId,
            NowPlayingShapeDto.EventName,
            shape,
            excludeWidgetId: null,
            channelEventId: null,
            cancellationToken
        );
    }
}
