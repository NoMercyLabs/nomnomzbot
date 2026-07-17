// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Remembers, per broadcaster, that the badge-bearing app-token chat send was rejected — so a channel
/// where the broadcaster never granted <c>channel:bot</c> and the bot isn't a moderator doesn't pay a
/// doomed extra Helix call on EVERY message. Entries expire, so granting the scope or modding the bot
/// restores the badge without a restart.
/// </summary>
public interface IHelixBadgeSendGate
{
    /// <summary>True while the broadcaster's last app-token rejection is still fresh — skip the attempt.</summary>
    bool IsBlocked(Guid broadcasterId);

    /// <summary>Records an app-token rejection for this broadcaster; the block expires after the TTL.</summary>
    void Block(Guid broadcasterId);

    /// <summary>Drops the block (a succeeded app-token send is proof the channel is eligible again).</summary>
    void Clear(Guid broadcasterId);
}
