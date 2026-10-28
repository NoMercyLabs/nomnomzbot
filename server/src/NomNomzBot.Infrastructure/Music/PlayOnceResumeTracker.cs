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

using System.Collections.Concurrent;
using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>In-memory <see cref="IPlayOnceResumeTracker"/> — mirrors the same singleton,
/// per-channel-dictionary shape <see cref="LastActiveSpotifyDeviceTracker"/> and
/// <see cref="NowPlayingCache"/> already use for this kind of process-local playback state.</summary>
public sealed class PlayOnceResumeTracker : IPlayOnceResumeTracker
{
    private readonly ConcurrentDictionary<Guid, PlayOnceResumeState> _pending = new();

    public void Remember(Guid broadcasterId, PlayOnceResumeState state) =>
        _pending[broadcasterId] = state;

    public bool TryPeek(Guid broadcasterId, out PlayOnceResumeState state) =>
        _pending.TryGetValue(broadcasterId, out state!);

    public bool TryTake(Guid broadcasterId, out PlayOnceResumeState state) =>
        _pending.TryRemove(broadcasterId, out state!);
}
