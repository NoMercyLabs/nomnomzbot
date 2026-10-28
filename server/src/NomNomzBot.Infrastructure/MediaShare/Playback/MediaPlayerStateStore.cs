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
using NomNomzBot.Application.MediaShare.Dtos;

namespace NomNomzBot.Infrastructure.MediaShare.Playback;

/// <summary>What each channel's media-share player last reported. A channel with no report is idle.</summary>
public interface IMediaPlayerStateStore
{
    MediaPlayerStateDto Get(Guid broadcasterId);

    void Set(Guid broadcasterId, MediaPlayerStateDto state);
}

public sealed class MediaPlayerStateStore : IMediaPlayerStateStore
{
    private readonly ConcurrentDictionary<Guid, MediaPlayerStateDto> _states = new();

    public MediaPlayerStateDto Get(Guid broadcasterId) =>
        _states.GetValueOrDefault(broadcasterId, MediaPlayerStateDto.Idle);

    public void Set(Guid broadcasterId, MediaPlayerStateDto state) =>
        _states[broadcasterId] = state;
}
