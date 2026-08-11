// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.AutomationApi.Events;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Bridges the internal <see cref="PlaybackStateChangedEvent"/> (which the overlay now-playing widget
/// already consumes, widget-sdk.md §9) into the public automation <see cref="SongChangedEvent"/>
/// (music-automation-controls.md D4). Builds the shared position-anchor shape straight from the
/// INCOMING event — the same fully-enriched data <c>PlaybackStateBroadcastHandler</c> already pushes
/// to the dashboard — rather than re-reading the provider a second time. An earlier version re-read
/// via <c>IMusicService.GetActiveProviderKeyAsync</c>/<c>GetNowPlayingAsync</c> "for freshness", but
/// that second live read could race the provider (e.g. right at a track boundary) and come back null,
/// silently dropping the <c>song.changed</c> push for that tick with no log line — while the sibling
/// dashboard handler, which has no such re-read, still fired. Building from the event removes the race
/// entirely: whatever data was fresh enough to trigger this handler is fresh enough to publish.
/// </summary>
public sealed class SongChangedProjector : IEventHandler<PlaybackStateChangedEvent>
{
    private readonly IMusicProviderManageApi _musicManageApi;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _clock;

    public SongChangedProjector(
        IMusicProviderManageApi musicManageApi,
        IEventBus eventBus,
        TimeProvider clock
    )
    {
        _musicManageApi = musicManageApi;
        _eventBus = eventBus;
        _clock = clock;
    }

    public async Task HandleAsync(
        PlaybackStateChangedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;
        // Nothing playing / no active provider — the same "skip" the old re-read applied when
        // GetNowPlayingAsync came back null, now decided from the event's own fields instead of a
        // second live call.
        if (@event.Provider is null || @event.TrackName is null)
            return;

        NowPlaying nowPlaying = new(
            @event.TrackName,
            @event.Artist,
            @event.Album,
            @event.AlbumArtUrl,
            @event.DurationMs,
            @event.ProgressMs,
            @event.IsPlaying,
            @event.VolumePercent,
            null,
            @event.Provider,
            @event.TrackUri,
            @event.ShuffleEnabled,
            @event.RepeatMode,
            @event.ArtistId
        );

        AutomationNowPlayingDto payload = await MusicAutomationProjection.ToNowPlayingAsync(
            nowPlaying,
            @event.Provider,
            @event.BroadcasterId,
            _musicManageApi,
            _clock,
            cancellationToken
        );

        await _eventBus.PublishAsync(
            new SongChangedEvent
            {
                BroadcasterId = @event.BroadcasterId,
                OccurredAt = payload.ServerTime,
                Title = payload.Title,
                Artist = payload.Artist,
                DurationMs = payload.DurationMs,
                PositionMs = payload.PositionMs,
                IsPlaying = payload.IsPlaying,
                ShuffleEnabled = payload.ShuffleEnabled,
                RepeatMode = payload.RepeatMode,
                IsSaved = payload.IsSaved,
                VolumePercent = payload.VolumePercent,
                AlbumArtUrl = payload.AlbumArtUrl,
            },
            cancellationToken
        );
    }
}
