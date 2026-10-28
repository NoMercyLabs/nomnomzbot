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
/// Optional provider surface for a player the streamer must have open before anything can play (YouTube's
/// overlay player). A request for such a provider is refused with a reason while no player is attached,
/// and no other engine is silenced for it.
/// </summary>
public interface IMusicProviderPlayerGate
{
    /// <summary>True when a player is attached right now and can play the next item.</summary>
    Task<bool> HasPlayerAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
}
