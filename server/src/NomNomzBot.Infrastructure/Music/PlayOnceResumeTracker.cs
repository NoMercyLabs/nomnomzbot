// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
