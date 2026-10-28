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
