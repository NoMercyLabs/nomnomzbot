// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Chat.Events;

/// <summary>
/// Published for EventSub <c>channel.chat.clear_user_messages</c>: a moderator purged every message from a single
/// chatter (a targeted clear, distinct from <see cref="ChatClearedEvent"/> which wipes the whole channel).
/// </summary>
public sealed class ChatUserMessagesClearedEvent : DomainEventBase
{
    public required string TargetUserId { get; init; }
    public required string TargetUserDisplayName { get; init; }
    public required string TargetUserLogin { get; init; }
}
