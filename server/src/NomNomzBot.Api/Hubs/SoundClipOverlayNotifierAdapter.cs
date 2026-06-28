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
            new PlaySoundPayload(playback.PlaybackUrl, playback.Volume, null),
            ct
        );

    public Task StopSoundAsync(
        Guid broadcasterId,
        string? handle,
        bool all,
        CancellationToken ct = default
    ) => _notifier.StopSoundAsync(broadcasterId.ToString(), new StopSoundPayload(handle, all), ct);
}
