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

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts music playback state changes to dashboard clients.</summary>
public sealed class PlaybackStateBroadcastHandler : IEventHandler<PlaybackStateChangedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public PlaybackStateBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(PlaybackStateChangedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        MusicTrackDto? track = @event.TrackName is not null
            ? new MusicTrackDto(
                @event.TrackName,
                @event.Artist ?? string.Empty,
                @event.Album ?? string.Empty,
                @event.AlbumArtUrl,
                @event.DurationMs,
                @event.Provider ?? "unknown",
                @event.ProgressMs,
                @event.ObservedAt.ToString("O")
            )
            : null;

        return _notifier.SendMusicStateAsync(
            @event.BroadcasterId.ToString(),
            new(@event.IsPlaying, track),
            ct
        );
    }
}
