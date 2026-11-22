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

namespace NomNomzBot.Infrastructure.Stream.Clips;

/// <summary>
/// Finds the clips that are created while a channel is live. Twitch has no EventSub event for a created clip, so each
/// live channel's clips are read about once a minute. The first read of a stream only learns the clips that exist;
/// every later read announces each clip it has not seen yet, once.
/// </summary>
public interface IClipDetector
{
    /// <summary>One pass over every live channel: read the clips when the channel is due, then announce the new ones.</summary>
    Task ProcessAsync(CancellationToken cancellationToken);
}
