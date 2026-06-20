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
    public required string RequestId { get; init; }
    public required string UserId { get; init; }
    public required string UserDisplayName { get; init; }
    public required string UserLogin { get; init; }
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
    public required string RequestId { get; init; }
    public required string UserId { get; init; }
    public required string UserDisplayName { get; init; }
    public required string ModeratorId { get; init; }
    public required string ModeratorDisplayName { get; init; }
    public required string Status { get; init; }
    public required string ResolutionText { get; init; }
}
