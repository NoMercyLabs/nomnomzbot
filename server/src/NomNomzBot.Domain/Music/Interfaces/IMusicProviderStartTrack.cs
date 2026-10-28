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
/// Optional provider surface for starting exactly one named track on an idle player. Unlike
/// <see cref="IMusicProviderPlayNow"/> it may replace the player's current context (a Spotify playlist),
/// so it is for a song request that takes over an idle player, never for a one-off interruption that must
/// leave the context in place. The provider's own waiting queue is not consulted, so a stray queued track
/// can never start in its place.
/// </summary>
public interface IMusicProviderStartTrack
{
    /// <summary>Starts the track now. False when the provider could not start it.</summary>
    Task<bool> StartTrackAsync(
        Guid broadcasterId,
        string trackUri,
        CancellationToken cancellationToken = default
    );
}
