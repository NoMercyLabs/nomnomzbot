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

namespace NomNomzBot.Domain.Music.Interfaces;

/// <summary>
/// Optional provider surface for a player that takes "play this track now" directly, without going through
/// a queue push followed by a skip. The provider's own waiting queue stays untouched, so a request that was
/// waiting still plays after the track.
/// </summary>
public interface IMusicProviderPlayNow
{
    /// <summary>Plays the track now. False when it cannot be played (unknown track, no player open).</summary>
    Task<bool> PlayNowAsync(
        Guid broadcasterId,
        string trackUri,
        CancellationToken cancellationToken = default
    );
}
