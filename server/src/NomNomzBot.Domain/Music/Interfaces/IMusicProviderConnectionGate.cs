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
/// A provider whose player commands return silently when the channel has no usable connection. Lets a caller
/// find out first, instead of mistaking the silent return for a command that worked.
/// </summary>
public interface IMusicProviderConnectionGate
{
    /// <summary>True when the channel has a usable access token for this provider right now.</summary>
    Task<bool> IsConnectedAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
}
