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
