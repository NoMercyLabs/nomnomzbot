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
