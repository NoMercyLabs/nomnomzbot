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
/// Published for EventSub <c>channel.chat.user_message_hold</c>: a chatter's message was held for moderator review
/// (AutoMod). The held message is not yet visible in chat — a moderation decision arrives later as
/// <see cref="ChatUserMessageUpdatedEvent"/>.
/// </summary>
public sealed class ChatUserMessageHeldEvent : DomainEventBase
{
    public required string UserId { get; init; }
    public required string UserDisplayName { get; init; }
    public required string UserLogin { get; init; }
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
    public required string UserId { get; init; }
    public required string UserDisplayName { get; init; }
    public required string UserLogin { get; init; }
    public required string MessageId { get; init; }

    /// <summary>The resolution: <c>approved</c> | <c>denied</c> | <c>invalid</c>.</summary>
    public required string Status { get; init; }

    /// <summary>The message text (concatenated fragments).</summary>
    public required string Text { get; init; }
}
