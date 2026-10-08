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
