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
