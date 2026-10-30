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
/// The one door to a channel's playback director: every control surface and every engine sends its
/// <see cref="PlaybackInput"/> here and changes neither the queue nor an engine itself.
/// </summary>
public interface IPlaybackInputSink
{
    /// <summary>
    /// Hands one input for the channel to its playback director, which applies it in order. The outcome says
    /// whether the director acted on it or ignored it in its current state.
    /// </summary>
    Task<PlaybackInputOutcome> SendAsync(
        Guid broadcasterId,
        PlaybackInput input,
        CancellationToken ct = default
    );
}
