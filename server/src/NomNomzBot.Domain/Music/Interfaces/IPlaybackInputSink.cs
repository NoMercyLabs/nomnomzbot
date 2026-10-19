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
/// The one door to a channel's playback director: every control surface and every engine sends its
/// <see cref="PlaybackInput"/> here and changes neither the queue nor an engine itself.
/// </summary>
public interface IPlaybackInputSink
{
    /// <summary>Hands one input for the channel to its playback director, which applies it in order.</summary>
    Task SendAsync(Guid broadcasterId, PlaybackInput input, CancellationToken ct = default);
}
