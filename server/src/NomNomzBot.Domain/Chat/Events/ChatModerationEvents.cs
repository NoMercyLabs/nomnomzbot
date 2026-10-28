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

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Chat.Events;

/// <summary>
/// Published for EventSub <c>channel.chat.clear_user_messages</c>: a moderator purged every message from a single
/// chatter (a targeted clear, distinct from <see cref="ChatClearedEvent"/> which wipes the whole channel).
/// </summary>
public sealed class ChatUserMessagesClearedEvent : DomainEventBase
{
    /// <summary>The Twitch user id (a number as text) of the viewer whose messages were cleared.</summary>
    public required string TargetUserId { get; init; }

    /// <summary>The name of that viewer as shown in chat.</summary>
    public required string TargetUserDisplayName { get; init; }

    /// <summary>The login name of that viewer, in lowercase.</summary>
    public required string TargetUserLogin { get; init; }
}
