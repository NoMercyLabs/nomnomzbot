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

namespace NomNomzBot.Domain.Commands.Events;

/// <summary>
/// An operator changed the scheduled job queue (today: retried an expired job as a new attempt). The inherited
/// <c>BroadcasterId</c> is the tenant that owns the job. The operator hub pushes it so every open scheduled
/// jobs page reloads.
/// </summary>
public sealed class ScheduledJobsChangedEvent : DomainEventBase
{
    /// <summary><c>retried</c>.</summary>
    public required string Change { get; init; }

    /// <summary>The job that was retried.</summary>
    public required Guid OriginalTaskId { get; init; }

    /// <summary>The new attempt appended to the queue.</summary>
    public required Guid NewTaskId { get; init; }
}
