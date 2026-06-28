// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Sound.Services;

namespace NomNomzBot.Infrastructure.Sound;

/// <summary>
/// No-op fallback for <see cref="ISoundClipOverlayNotifier"/> used in contexts where no live overlay
/// connection exists (worker processes, background services, tests). The API host replaces this with the
/// SignalR-backed adapter (<c>SoundClipOverlayNotifierAdapter</c>) via a later service registration.
/// </summary>
internal sealed class NullSoundClipOverlayNotifier : ISoundClipOverlayNotifier
{
    public Task PlaySoundAsync(
        Guid broadcasterId,
        SoundPlaybackDto playback,
        CancellationToken ct = default
    ) => Task.CompletedTask;

    public Task StopSoundAsync(
        Guid broadcasterId,
        string? handle,
        bool all,
        CancellationToken ct = default
    ) => Task.CompletedTask;
}
