// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Stream.EventHandlers;

/// <summary>
/// Hands every ad break that begins to the <see cref="IAdBreakEndScheduler"/>, which tells chat when it is over.
/// It only schedules, so the begin event never waits for the break to run its course.
/// </summary>
public sealed class AdBreakEndScheduleHandler : IEventHandler<AdBreakBeganEvent>
{
    private readonly IAdBreakEndScheduler _scheduler;

    public AdBreakEndScheduleHandler(IAdBreakEndScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    public Task HandleAsync(AdBreakBeganEvent @event, CancellationToken ct = default)
    {
        _scheduler.Schedule(@event.BroadcasterId, @event.StartedAt, @event.DurationSeconds);
        return Task.CompletedTask;
    }
}
