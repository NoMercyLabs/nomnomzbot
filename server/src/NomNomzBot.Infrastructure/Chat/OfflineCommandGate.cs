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

using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// The offline switch for chat commands. A channel that has not opted to keep commands active while offline
/// (<see cref="ChannelContext.KeepActiveWhileOffline"/>) answers no viewer command while it is not live. The
/// broadcaster and moderators are exempt, so they can still run and test commands off stream. Built-in and
/// authored commands pass the same gate; the caller decides which commands are exempt by law (reserved built-ins).
/// </summary>
public static class OfflineCommandGate
{
    public static bool Blocks(ChannelContext channel, ChatMessageReceivedEvent chat) =>
        channel is { KeepActiveWhileOffline: false, IsLive: false }
        && chat is { IsBroadcaster: false, IsModerator: false };
}
