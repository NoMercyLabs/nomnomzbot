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
/// Optional provider surface for a player the streamer must have open before anything can play (YouTube's
/// overlay player). A request for such a provider is refused with a reason while no player is attached,
/// and no other engine is silenced for it.
/// </summary>
public interface IMusicProviderPlayerGate
{
    /// <summary>True when a player is attached right now and can play the next item.</summary>
    Task<bool> HasPlayerAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
}
