// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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

    Task StopSoundAsync(
        Guid broadcasterId,
        string? handle,
        bool all,
        CancellationToken ct = default
    );
}
