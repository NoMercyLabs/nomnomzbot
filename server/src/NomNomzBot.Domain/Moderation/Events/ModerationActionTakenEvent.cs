// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Moderation.Events;

using Platform;

/// <summary>When a moderation action is taken against a viewer in the channel.</summary>
public sealed class ModerationActionTakenEvent : DomainEventBase
{
    /// <summary>The Twitch id of the channel where the action happened (a number as text).</summary>
    public required string ChannelId { get; init; }

    /// <summary>The Twitch user id of the moderator who took the action (a number as text).</summary>
    public required string ModeratorId { get; init; }

    /// <summary>The Twitch user id of the viewer the action was taken against (a number as text).</summary>
    public required string TargetUserId { get; init; }

    /// <summary>The kind of action, as Twitch names it, for example ban or timeout.</summary>
    public required string ActionType { get; init; }

    /// <summary>The reason given for the action. Empty when no reason was given.</summary>
    public required string? Reason { get; init; }

    /// <summary>The display name of the moderator. Empty when the source did not name them.</summary>
    public string ModeratorDisplayName { get; init; } = string.Empty;

    /// <summary>The id of the chat message a <c>delete</c> action removed; null for every other action.</summary>
    public string? MessageId { get; init; }
}
