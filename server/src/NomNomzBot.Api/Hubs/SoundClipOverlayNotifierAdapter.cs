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

using NomNomzBot.Application.Sound.Services;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// Adapts the Application-layer <see cref="ISoundClipOverlayNotifier"/> abstraction to the
/// <see cref="IWidgetNotifier"/> SignalR hub — bridges the Infrastructure→API dependency boundary so
/// <c>SoundClipService</c> never takes a direct reference to the SignalR layer.
/// </summary>
internal sealed class SoundClipOverlayNotifierAdapter : ISoundClipOverlayNotifier
{
    private readonly IWidgetNotifier _notifier;

    public SoundClipOverlayNotifierAdapter(IWidgetNotifier notifier)
    {
        _notifier = notifier;
    }

    public Task PlaySoundAsync(
        Guid broadcasterId,
        SoundPlaybackDto playback,
        CancellationToken ct = default
    ) =>
        _notifier.PlaySoundAsync(
            broadcasterId.ToString(),
            new(playback.PlaybackUrl, playback.Volume, playback.Handle, playback.Synced),
            ct
        );

    public Task PlayTtsAsync(
        Guid broadcasterId,
        SoundPlaybackDto playback,
        CancellationToken ct = default
    ) =>
        _notifier.PlayTtsClipAsync(
            broadcasterId.ToString(),
            new(playback.PlaybackUrl, playback.Volume, playback.Handle, playback.Synced),
            ct
        );

    public Task StopSoundAsync(
        Guid broadcasterId,
        string? handle,
        bool all,
        CancellationToken ct = default
    ) => _notifier.StopSoundAsync(broadcasterId.ToString(), new(handle, all), ct);
}
