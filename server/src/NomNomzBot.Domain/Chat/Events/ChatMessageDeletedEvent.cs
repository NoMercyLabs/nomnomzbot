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

/// <summary>When a single chat message is deleted.</summary>
public sealed class ChatMessageDeletedEvent : DomainEventBase
{
    /// <summary>The id of the deleted message.</summary>
    public required string MessageId { get; init; }

    /// <summary>The Twitch user id (a number as text) of the moderator who deleted the message.</summary>
    public required string DeletedByUserId { get; init; }

    /// <summary>The Twitch user id (a number as text) of the viewer who wrote the deleted message.</summary>
    public required string TargetUserId { get; init; }
}
