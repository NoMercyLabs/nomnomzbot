// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Skips a retracted song the moment the provider starts it. A request that was already pushed to the
/// provider's own queue cannot be removed from there (Spotify has no such call), so
/// <see cref="MusicService.RemoveFromQueueAsync"/> arms a marker instead; this handler consumes it when
/// the live playback state reports that track and advances the player. Each marker fires once.
/// </summary>
public sealed class SongRetractionSkipHandler : IEventHandler<PlaybackStateChangedEvent>
{
    private readonly ISongRequestQueueStore _queueStore;
    private readonly IMusicService _music;
    private readonly ILogger<SongRetractionSkipHandler> _logger;

    public SongRetractionSkipHandler(
        ISongRequestQueueStore queueStore,
        IMusicService music,
        ILogger<SongRetractionSkipHandler> logger
    )
    {
        _queueStore = queueStore;
        _music = music;
        _logger = logger;
    }

    public async Task HandleAsync(
        PlaybackStateChangedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty || string.IsNullOrEmpty(@event.TrackUri))
            return;

        string broadcasterId = @event.BroadcasterId.ToString();
        if (!_queueStore.TryConsumeRetraction(broadcasterId, @event.TrackUri))
            return;

        Result skipped = await _music.SkipAsync(
            broadcasterId,
            string.Empty,
            cancellationToken: cancellationToken
        );
        if (!skipped.IsSuccess)
            _logger.LogWarning(
                "Retracted track {TrackUri} started for {BroadcasterId} but the skip failed: {Error}",
                @event.TrackUri,
                broadcasterId,
                skipped.ErrorMessage
            );
    }
}
