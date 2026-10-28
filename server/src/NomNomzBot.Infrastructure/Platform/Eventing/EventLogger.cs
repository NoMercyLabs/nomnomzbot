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
