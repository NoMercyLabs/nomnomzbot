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
