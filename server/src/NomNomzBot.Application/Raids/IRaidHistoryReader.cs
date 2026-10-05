// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Raids;

/// <summary>Reads who raided whom, from the event journal, for one channel.</summary>
public interface IRaidHistoryReader
{
    /// <summary>
    /// Raid history of <paramref name="channelId"/>, keyed by the remote channel's Twitch user id: raids we sent
    /// (<see cref="RaidHistory.Outgoing"/>) and raids we received (<see cref="RaidHistory.Incoming"/>).
    /// <paramref name="ownTwitchUserId"/> tells the two directions apart.
    /// </summary>
    Task<Result<RaidHistory>> GetHistoryAsync(
        Guid channelId,
        string ownTwitchUserId,
        CancellationToken cancellationToken = default
    );
}
