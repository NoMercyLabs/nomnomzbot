// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// Structured logging for domain events. Logs event metadata for debugging and tracing.
/// Registered as a singleton.
/// </summary>
public sealed class EventLogger
{
    private readonly ILogger<EventLogger> _logger;

    public EventLogger(ILogger<EventLogger> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Logs a domain event with structured properties for correlation and filtering.
    /// </summary>
    public void Log<TEvent>(TEvent @event)
        where TEvent : IDomainEvent
    {
        _logger.LogInformation(
            "DomainEvent {EventType} published. EventId={EventId}, BroadcasterId={BroadcasterId}, Timestamp={Timestamp}",
            typeof(TEvent).Name,
            @event.EventId,
            @event.BroadcasterId == Guid.Empty ? "(platform)" : @event.BroadcasterId.ToString(),
            @event.OccurredAt
        );
    }
}
