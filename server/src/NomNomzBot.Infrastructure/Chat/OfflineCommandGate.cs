// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
