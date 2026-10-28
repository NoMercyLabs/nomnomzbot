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
/// A provider whose player commands return silently when the channel has no usable connection. Lets a caller
/// find out first, instead of mistaking the silent return for a command that worked.
/// </summary>
public interface IMusicProviderConnectionGate
{
    /// <summary>True when the channel has a usable access token for this provider right now.</summary>
    Task<bool> IsConnectedAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
}
