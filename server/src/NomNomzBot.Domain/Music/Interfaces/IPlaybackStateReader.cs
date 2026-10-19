// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Domain.Music.Interfaces;

/// <summary>
/// The read-only view of a channel's playback director: callers see its state and change nothing.
/// Every change still goes through <see cref="IPlaybackInputSink"/>.
/// </summary>
public interface IPlaybackStateReader
{
    /// <summary>
    /// The channel director's current state. A channel that has no director yet is
    /// <see cref="PlaybackState.Idle"/>; reading never creates a director.
    /// </summary>
    PlaybackState StateOf(Guid channelId);

    /// <summary>
    /// The channel director's music volume and mute. A channel that has no director yet is
    /// <see cref="PlaybackLevel.Default"/>.
    /// </summary>
    PlaybackLevel LevelOf(Guid channelId);
}
