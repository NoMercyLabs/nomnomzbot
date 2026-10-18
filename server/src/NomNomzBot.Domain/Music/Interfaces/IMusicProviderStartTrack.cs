// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
