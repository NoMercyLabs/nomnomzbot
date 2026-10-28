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
/// Published for EventSub <c>channel.chat.user_message_hold</c>: a chatter's message was held for moderator review
/// (AutoMod). The held message is not yet visible in chat — a moderation decision arrives later as
/// <see cref="ChatUserMessageUpdatedEvent"/>.
/// </summary>
public sealed class ChatUserMessageHeldEvent : DomainEventBase
{
    /// <summary>The Twitch user id (a number as text) of the viewer who wrote the held message.</summary>
    public required string UserId { get; init; }

    /// <summary>The name of that viewer as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>The login name of that viewer, in lowercase.</summary>
    public required string UserLogin { get; init; }

    /// <summary>The id of the held message.</summary>
    public required string MessageId { get; init; }

    /// <summary>The held message text (concatenated fragments).</summary>
    public required string Text { get; init; }
}

/// <summary>
/// Published for EventSub <c>channel.chat.user_message_update</c>: a moderation decision resolved a previously held
/// message (see <see cref="ChatUserMessageHeldEvent"/>). <see cref="Status"/> is <c>approved</c>, <c>denied</c>, or
/// <c>invalid</c>.
/// </summary>
public sealed class ChatUserMessageUpdatedEvent : DomainEventBase
{
    /// <summary>The Twitch user id (a number as text) of the viewer who wrote the message.</summary>
    public required string UserId { get; init; }

    /// <summary>The name of that viewer as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>The login name of that viewer, in lowercase.</summary>
    public required string UserLogin { get; init; }

    /// <summary>The id of the held message.</summary>
    public required string MessageId { get; init; }

    /// <summary>The resolution: <c>approved</c> | <c>denied</c> | <c>invalid</c>.</summary>
    public required string Status { get; init; }

    /// <summary>The message text (concatenated fragments).</summary>
    public required string Text { get; init; }
}
