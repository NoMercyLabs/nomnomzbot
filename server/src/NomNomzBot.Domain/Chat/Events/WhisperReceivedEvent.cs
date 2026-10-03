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
/// Published when the authorized user receives a whisper (EventSub <c>user.whisper.message</c>). This is a
/// user-scoped event — the recipient (<see cref="ToUserId"/>) is the tenant, not a channel; the publisher sets
/// <c>BroadcasterId</c> to the dispatcher-resolved tenant (may be the platform sentinel for a pure user flow).
/// </summary>
public sealed class WhisperReceivedEvent : DomainEventBase
{
    /// <summary>The id of the whisper.</summary>
    public required string WhisperId { get; init; }

    /// <summary>The Twitch user id (a number as text) of the viewer who sent the whisper.</summary>
    public required string FromUserId { get; init; }

    /// <summary>The name of the sender as shown in chat.</summary>
    public required string FromUserDisplayName { get; init; }

    /// <summary>The login name of the sender, in lowercase.</summary>
    public required string FromUserLogin { get; init; }

    /// <summary>The Twitch user id (a number as text) of the account that received the whisper.</summary>
    public required string ToUserId { get; init; }

    /// <summary>The whisper body (EventSub nests this under <c>whisper.text</c>).</summary>
    public required string Text { get; init; }
}
