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

using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// Fires a sample event of one type through the channel's event response and its pipeline, so the operator sees
/// what viewers would get without waiting for the real event. Presentation only, like an activity replay: the
/// chat line, the speech, the overlay alerts and the pipeline run; it never grants currency or loyalty, moves a
/// counter, stat or hype state, publishes the event, or writes an activity or journal row.
/// </summary>
public interface IEventResponseTestService
{
    /// <param name="broadcasterId">The tenant channel.</param>
    /// <param name="eventType">An event type of the response table (<see cref="EventResponsePresetCatalog"/>).</param>
    /// <param name="callerUserId">The signed-in user, who is the acting user of the sample event.</param>
    /// <returns>What was presented, with warning codes; NOT_FOUND for an unknown event type or caller.</returns>
    Task<Result<EventResponseTestResult>> FireAsync(
        Guid broadcasterId,
        string eventType,
        Guid callerUserId,
        CancellationToken ct = default
    );
}
