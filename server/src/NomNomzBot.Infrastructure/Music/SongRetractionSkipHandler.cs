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
    private readonly Func<TimeSpan, CancellationToken, Task> _wait;

    // Spotify refuses a skip for a moment while the player changes track, so a refused skip is tried again.
    private const int MaxTries = 4;
    private static readonly TimeSpan RetryWait = TimeSpan.FromMilliseconds(1500);

    public SongRetractionSkipHandler(
        ISongRequestQueueStore queueStore,
        IMusicService music,
        ILogger<SongRetractionSkipHandler> logger,
        Func<TimeSpan, CancellationToken, Task>? wait = null
    )
    {
        _queueStore = queueStore;
        _music = music;
        _logger = logger;
        _wait = wait ?? Task.Delay;
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
        for (int attempt = 1; !skipped.IsSuccess && attempt < MaxTries; attempt++)
        {
            await _wait(RetryWait, cancellationToken);
            skipped = await _music.SkipAsync(
                broadcasterId,
                string.Empty,
                cancellationToken: cancellationToken
            );
        }

        if (!skipped.IsSuccess)
            _logger.LogWarning(
                "Retracted track {TrackUri} started for {BroadcasterId} but the skip failed: {Error}",
                @event.TrackUri,
                broadcasterId,
                skipped.ErrorMessage
            );
    }
}
