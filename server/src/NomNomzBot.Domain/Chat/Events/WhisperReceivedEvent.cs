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
