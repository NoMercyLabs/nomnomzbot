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

// Shared-chat session domain events (EventSub channel.shared_chat.*). A shared-chat session merges several
// channels' chats under one host; these carry the session identity and the participating broadcaster ids so
// downstream features can attribute cross-channel messages. Each inherits EventId / Timestamp / BroadcasterId
// from DomainEventBase (set by the publisher to the resolved tenant).

/// <summary>Published when a shared-chat session begins (<c>channel.shared_chat.begin</c>).</summary>
public sealed class SharedChatBeganEvent : DomainEventBase
{
    public required string SessionId { get; init; }
    public required string HostBroadcasterId { get; init; }
    public required string HostBroadcasterDisplayName { get; init; }
    public required string HostBroadcasterLogin { get; init; }

    /// <summary>The participating broadcaster ids (includes the host), in payload order.</summary>
    public required IReadOnlyList<string> Participants { get; init; }
}

/// <summary>Published when a shared-chat session's participant set changes (<c>channel.shared_chat.update</c>).</summary>
public sealed class SharedChatUpdatedEvent : DomainEventBase
{
    public required string SessionId { get; init; }
    public required string HostBroadcasterId { get; init; }

    /// <summary>The current participating broadcaster ids (includes the host), in payload order.</summary>
    public required IReadOnlyList<string> Participants { get; init; }
}

/// <summary>Published when a shared-chat session ends (<c>channel.shared_chat.end</c>).</summary>
public sealed class SharedChatEndedEvent : DomainEventBase
{
    public required string SessionId { get; init; }
    public required string HostBroadcasterId { get; init; }
}
