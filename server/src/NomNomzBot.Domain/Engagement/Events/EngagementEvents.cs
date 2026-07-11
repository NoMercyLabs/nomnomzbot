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

namespace NomNomzBot.Domain.Engagement.Events;

// DomainEventBase is a class, so these are sealed CLASSES (records may not inherit a non-record class).
// BroadcasterId (the tenant) is inherited from DomainEventBase.

/// <summary>A viewer's first-ever message in this channel while live (engagement.md D1). Trigger kind
/// <c>engagement.first_time_chatter</c>.</summary>
public sealed class FirstTimeChatterDetectedEvent : DomainEventBase
{
    public required Guid ViewerUserId { get; init; }
    public required string ViewerExternalUserId { get; init; }
    public required string ViewerDisplayName { get; init; }
}

/// <summary>A viewer's first message this stream, having chatted here before (engagement.md D1). Trigger
/// kind <c>engagement.returning_chatter</c>.</summary>
public sealed class ReturningChatterDetectedEvent : DomainEventBase
{
    public required Guid ViewerUserId { get; init; }
    public required string ViewerExternalUserId { get; init; }
    public required string ViewerDisplayName { get; init; }
    public required int DaysSinceLastSeen { get; init; }
}

/// <summary>A viewer hit a configured consecutive-stream milestone (engagement.md D1). Trigger kind
/// <c>engagement.watch_streak</c>.</summary>
public sealed class WatchStreakMilestoneEvent : DomainEventBase
{
    public required Guid ViewerUserId { get; init; }
    public required string ViewerExternalUserId { get; init; }
    public required string ViewerDisplayName { get; init; }
    public required int StreakCount { get; init; }
}
