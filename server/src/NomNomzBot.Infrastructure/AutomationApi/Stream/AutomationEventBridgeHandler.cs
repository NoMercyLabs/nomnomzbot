// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.AutomationApi.Services;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.AutomationApi.Stream;

/// <summary>
/// The event bridge (automation-api.md §3/D6): one typed bus handler per DESCRIBED domain event —
/// registered by the same DI scan that discovers descriptors, so an event without a descriptor has
/// no handler and can never leak. Fans the PII-safe projection out to the locally-connected sessions
/// that subscribed to the wire name, hold scope <c>events</c>, and belong to the event's tenant.
/// A dead socket never breaks the fan-out (or the publisher).
/// </summary>
public sealed class AutomationEventBridgeHandler<TEvent> : IEventHandler<TEvent>
    where TEvent : DomainEventBase
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    private readonly IAutomationEventRegistry _events;
    private readonly IAutomationSessionRegistry _sessions;
    private readonly ILogger<AutomationEventBridgeHandler<TEvent>> _logger;

    public AutomationEventBridgeHandler(
        IAutomationEventRegistry events,
        IAutomationSessionRegistry sessions,
        ILogger<AutomationEventBridgeHandler<TEvent>> logger
    )
    {
        _events = events;
        _sessions = sessions;
        _logger = logger;
    }

    public async Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default)
    {
        if (!_events.TryGet(@event.GetType(), out IAutomationEventDescriptor? descriptor))
            return; // default-deny — only described events exist on this surface

        List<AutomationSession> targets =
        [
            .. _sessions
                .SubscribersOf(descriptor.PublicName)
                .Where(s =>
                    s.Principal.BroadcasterId == @event.BroadcasterId
                    && s.Principal.Scopes.Contains("events")
                ),
        ];
        if (targets.Count == 0)
            return;

        string frame = JsonSerializer.Serialize(
            new
            {
                op = "event",
                type = descriptor.PublicName,
                broadcasterId = @event.BroadcasterId,
                occurredAt = @event.OccurredAt,
                data = descriptor.ProjectPayload(@event),
            },
            WireJson
        );

        foreach (AutomationSession session in targets)
        {
            try
            {
                await session.SendAsync(frame, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Automation stream send failed for session {Session} ({Event}); the socket loop will reap it.",
                    session.SessionId,
                    descriptor.PublicName
                );
            }
        }
    }
}
