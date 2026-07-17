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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Vts.Events;

namespace NomNomzBot.Infrastructure.Vts.EventHandlers;

/// <summary>
/// The <c>vts_event</c> trigger surface (vtube-studio.md §4): every VTS event dispatches its bound
/// responses under <c>vts.&lt;EventType&gt;</c> — e.g. <c>vts.ModelLoadedEvent</c> — with the flat
/// payload fields as <c>{vts.event.&lt;field&gt;}</c> vars. High-volume events are already gated by
/// the subscription mask; nothing here writes the activity feed.
/// </summary>
public sealed class VtsEventTriggerSource : IEventHandler<VtsEventReceived>
{
    private readonly IEventResponseExecutor _executor;
    private readonly ILogger<VtsEventTriggerSource> _logger;

    public VtsEventTriggerSource(
        IEventResponseExecutor executor,
        ILogger<VtsEventTriggerSource> logger
    )
    {
        _executor = executor;
        _logger = logger;
    }

    public async Task HandleAsync(
        VtsEventReceived @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty || string.IsNullOrEmpty(@event.EventType))
            return;

        Dictionary<string, string> variables = BuildVariables(@event);
        try
        {
            await _executor.ExecuteAsync(
                @event.BroadcasterId,
                $"vts.{@event.EventType}",
                userId: null,
                userDisplayName: string.Empty,
                variables,
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "vts_event trigger dispatch failed for {Channel} ({EventType}).",
                @event.BroadcasterId,
                @event.EventType
            );
        }
    }

    internal static Dictionary<string, string> BuildVariables(VtsEventReceived e)
    {
        Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase)
        {
            ["vts.event.type"] = e.EventType,
        };
        try
        {
            using JsonDocument doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(e.PayloadJson) ? "{}" : e.PayloadJson
            );
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                foreach (JsonProperty property in doc.RootElement.EnumerateObject())
                    variables[$"vts.event.{property.Name}"] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                        JsonValueKind.Null => string.Empty,
                        _ => property.Value.ToString(),
                    };
        }
        catch (JsonException)
        {
            // A malformed payload still fires the trigger with the type var alone.
        }
        return variables;
    }
}
