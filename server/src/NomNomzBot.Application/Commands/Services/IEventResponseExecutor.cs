// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
}
