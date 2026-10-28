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

    public Task PlayTtsAsync(
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
