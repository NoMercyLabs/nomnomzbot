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

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// Executes the operator's configured <c>EventResponse</c> for an event-type key — THE one execution
/// path every trigger source dispatches through (Twitch alert handlers, supporter triggers,
/// stream online/offline, reward redemptions): look up the enabled row, then send the resolved
/// <c>chat_message</c> template or run the bound pipeline. A disabled/absent row or a <c>none</c>
/// response type is a no-op; execution failures are logged, never thrown back into the event bus.
/// </summary>
public interface IEventResponseExecutor
{
    /// <param name="broadcasterId">The tenant channel.</param>
    /// <param name="eventTypeKey">The configured event type (e.g. <c>channel.follow</c>, <c>stream.online</c>).</param>
    /// <param name="userId">The acting user's platform id, for pipeline attribution (null when the event has none).</param>
    /// <param name="userDisplayName">The acting user's display name, for pipeline attribution.</param>
    /// <param name="variables">The event's template variables, seeded by the trigger source.</param>
    /// <param name="cancellationToken">Cancels the lookup and the dispatched action.</param>
    Task ExecuteAsync(
        Guid broadcasterId,
        string eventTypeKey,
        string? userId,
        string? userDisplayName,
        Dictionary<string, string> variables,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Runs the SAME configured response as <see cref="ExecuteAsync"/> for a past event, as a dashboard replay.
    /// Presentation only: it never resumes a pipeline run parked on a <c>wait_for_event</c> step, because the
    /// event is not happening again. Parameters match <see cref="ExecuteAsync"/>.
    /// </summary>
    /// <returns>What the response put in front of viewers (chat lines, TTS, overlay pushes).</returns>
    Task<EventResponseOutcome> ReplayAsync(
        Guid broadcasterId,
        string eventTypeKey,
        string? userId,
        string? userDisplayName,
        Dictionary<string, string> variables,
        CancellationToken cancellationToken = default
    );
}
