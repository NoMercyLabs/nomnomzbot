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

namespace NomNomzBot.Application.Sound.Services;

/// <summary>
/// Abstraction that lets <c>SoundClipService</c> push play/stop signals to the overlay without taking a direct
/// dependency on the API layer's SignalR hub context. Implemented by <c>OverlayNotifierAdapter</c> in the API layer.
/// </summary>
public interface ISoundClipOverlayNotifier
{
    Task PlaySoundAsync(
        Guid broadcasterId,
        SoundPlaybackDto playback,
        CancellationToken ct = default
    );

    /// <summary>Plays a synthesized TTS clip on the channel's TTS page, never on the Audio source page.</summary>
    Task PlayTtsAsync(
        Guid broadcasterId,
        SoundPlaybackDto playback,
        CancellationToken ct = default
    );

    Task StopSoundAsync(
        Guid broadcasterId,
        string? handle,
        bool all,
        CancellationToken ct = default
    );
}
