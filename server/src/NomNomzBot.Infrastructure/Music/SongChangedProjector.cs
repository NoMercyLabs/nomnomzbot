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
/// (music-automation-controls.md D4). Re-reads the FULL live state through the same
/// <see cref="MusicAutomationProjection"/> the REST now-playing read uses, rather than forwarding the
/// thin play/pause-only payload — so a Stream Deck key subscribed to <c>song.changed</c> gets the
/// complete position-anchor shape, never a guess, and can never drift from <c>GetNowPlayingAsync</c>.
/// </summary>
public sealed class SongChangedProjector : IEventHandler<PlaybackStateChangedEvent>
{
    private readonly IMusicService _music;
    private readonly IMusicProviderManageApi _musicManageApi;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _clock;

    public SongChangedProjector(
        IMusicService music,
        IMusicProviderManageApi musicManageApi,
        IEventBus eventBus,
        TimeProvider clock
    )
    {
        _music = music;
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

        string broadcasterId = @event.BroadcasterId.ToString();
        string? provider = await _music.GetActiveProviderKeyAsync(broadcasterId, cancellationToken);
        NowPlaying? nowPlaying = await _music.GetNowPlayingAsync(broadcasterId, cancellationToken);
        if (provider is null || nowPlaying is null)
            return;

        AutomationNowPlayingDto payload = await MusicAutomationProjection.ToNowPlayingAsync(
            nowPlaying,
            provider,
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
            },
            cancellationToken
        );
    }
}
