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

    /// <summary>
    /// Why the channel director is blocked, as a <see cref="PlaybackBlockedReason"/> name. Null when it is not
    /// blocked, or the channel has no director yet.
    /// </summary>
    string? BlockedReasonOf(Guid channelId);
}
