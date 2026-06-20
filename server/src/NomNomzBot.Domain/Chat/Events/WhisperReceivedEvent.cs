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
    public required string WhisperId { get; init; }
    public required string FromUserId { get; init; }
    public required string FromUserDisplayName { get; init; }
    public required string FromUserLogin { get; init; }
    public required string ToUserId { get; init; }

    /// <summary>The whisper body (EventSub nests this under <c>whisper.text</c>).</summary>
    public required string Text { get; init; }
}
