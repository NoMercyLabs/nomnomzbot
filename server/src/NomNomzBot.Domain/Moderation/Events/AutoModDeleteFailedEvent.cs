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

namespace NomNomzBot.Domain.Moderation.Events;

/// <summary>
/// A moderation rule decided to delete a chat message, but the platform refused or failed the deletion. The
/// message is still on screen. This event exists so the failure is recorded and shown to the streamer in the
/// attention inbox instead of being lost in a log line.
/// </summary>
public sealed class AutoModDeleteFailedEvent : DomainEventBase
{
    /// <summary>The platform id of the chat message that could not be deleted.</summary>
    public required string MessageId { get; init; }

    /// <summary>The platform user id of the chatter who wrote the message (a number as text).</summary>
    public required string UserId { get; init; }

    /// <summary>The chatter's login name (lowercase).</summary>
    public required string UserLogin { get; init; }

    /// <summary>The name of the moderation rule that asked for the deletion.</summary>
    public required string RuleName { get; init; }

    /// <summary>The error the platform call returned.</summary>
    public required string Error { get; init; }
}
