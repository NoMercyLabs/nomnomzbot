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
/// A viewer opened an unban request in the channel (<c>channel.unban_request.create</c>). <see cref="RequestId"/>
/// is Twitch's unban-request id (used to correlate the later resolve), and <see cref="Text"/> is the viewer's
/// appeal message.
/// </summary>
public sealed class UnbanRequestCreatedEvent : DomainEventBase
{
    /// <summary>The Twitch id of the unban request.</summary>
    public required string RequestId { get; init; }

    /// <summary>The Twitch user id of the banned viewer who asked (a number as text).</summary>
    public required string UserId { get; init; }

    /// <summary>The viewer's display name, as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>The viewer's login name (lowercase).</summary>
    public required string UserLogin { get; init; }

    /// <summary>The message the viewer wrote to ask for the unban.</summary>
    public required string Text { get; init; }
}

/// <summary>
/// A moderator resolved an unban request (<c>channel.unban_request.resolve</c>). <see cref="Status"/> is the
/// outcome — <c>approved</c>, <c>denied</c>, or <c>canceled</c> — and <see cref="ResolutionText"/> is the
/// moderator's optional note. <see cref="RequestId"/> correlates back to the originating
/// <see cref="UnbanRequestCreatedEvent"/>.
/// </summary>
public sealed class UnbanRequestResolvedEvent : DomainEventBase
{
    /// <summary>The Twitch id of the unban request.</summary>
    public required string RequestId { get; init; }

    /// <summary>The Twitch user id of the viewer who asked (a number as text).</summary>
    public required string UserId { get; init; }

    /// <summary>The viewer's display name, as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>The Twitch user id of the moderator who decided (a number as text).</summary>
    public required string ModeratorId { get; init; }

    /// <summary>The moderator's display name, as shown in chat.</summary>
    public required string ModeratorDisplayName { get; init; }

    /// <summary>The result: approved, denied or canceled.</summary>
    public required string Status { get; init; }

    /// <summary>The note the moderator wrote with the decision. Empty when there is no note.</summary>
    public required string ResolutionText { get; init; }
}
