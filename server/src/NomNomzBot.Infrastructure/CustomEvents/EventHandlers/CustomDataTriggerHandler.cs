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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.CustomEvents.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.CustomEvents.EventHandlers;

/// <summary>
/// Closes the custom-events.md D1(a) gap: an ingested datum must ALSO fire a <c>custom.&lt;name&gt;</c>
/// pipeline/event-response trigger so a streamer's automation reacts to it (e.g. heart-rate &gt; 120 →
/// chat message). Until now a <see cref="CustomDataReceivedEvent"/> only re-broadcast to overlays
/// (<see cref="Api.Hubs.Broadcasters.CustomDataBroadcastHandler"/>). This handler is the direct analogue
/// of <see cref="Webhooks.EventHandlers.InboundWebhookAutomationBridge"/> for custom data: it routes each
/// datum to <see cref="IEventResponseExecutor"/> on the <c>custom.&lt;source name&gt;</c> event-type key,
/// seeding the extracted fields as <c>custom.&lt;name&gt;.&lt;field&gt;</c> template variables. The executor
/// is a no-op when the streamer has bound no matching enabled response, so this is safe unconditionally.
/// </summary>
public sealed class CustomDataTriggerHandler : IEventHandler<CustomDataReceivedEvent>
{
    private readonly IEventResponseExecutor _eventResponses;
    private readonly ILogger<CustomDataTriggerHandler> _logger;

    public CustomDataTriggerHandler(
        IEventResponseExecutor eventResponses,
        ILogger<CustomDataTriggerHandler> logger
    )
    {
        _eventResponses = eventResponses;
        _logger = logger;
    }

    public async Task HandleAsync(
        CustomDataReceivedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        // A tenant-less event drives no channel-scoped automation.
        if (@event.BroadcasterId == Guid.Empty)
            return;

        string eventTypeKey = $"custom.{@event.SourceName}";

        Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase)
        {
            ["custom.source"] = @event.SourceName,
        };
        foreach ((string key, string value) in @event.Fields)
            variables[$"custom.{@event.SourceName}.{key}"] = value;

        try
        {
            await _eventResponses.ExecuteAsync(
                @event.BroadcasterId,
                eventTypeKey,
                userId: null,
                userDisplayName: @event.SourceName,
                variables,
                cancellationToken
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A trigger fault never propagates back into the event bus (a fault must not break ingest).
            _logger.LogWarning(
                ex,
                "Custom data source {SourceName} failed to fire its {EventType} trigger on {Channel}.",
                @event.SourceName,
                eventTypeKey,
                @event.BroadcasterId
            );
        }
    }
}
