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
