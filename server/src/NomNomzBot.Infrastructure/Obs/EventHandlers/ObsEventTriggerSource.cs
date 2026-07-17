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
using NomNomzBot.Domain.Obs.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Obs.EventHandlers;

/// <summary>
/// The <c>obs_event</c> trigger surface (obs-control.md §6): every OBS event that arrives (direct
/// socket or leader bridge) dispatches its bound responses under the key
/// <c>obs.&lt;EventType&gt;</c> — e.g. <c>obs.CurrentProgramSceneChanged</c> — with the event's flat
/// fields exposed as <c>{obs.event.&lt;field&gt;}</c> template vars. OBS events can be high-volume
/// (scene flips, media ticks), so nothing is written to the activity feed here — binding a response
/// is the opt-in.
/// </summary>
public sealed class ObsEventTriggerSource : IEventHandler<ObsEventReceivedEvent>
{
    private readonly IEventResponseExecutor _executor;
    private readonly ILogger<ObsEventTriggerSource> _logger;

    public ObsEventTriggerSource(
        IEventResponseExecutor executor,
        ILogger<ObsEventTriggerSource> logger
    )
    {
        _executor = executor;
        _logger = logger;
    }

    public async Task HandleAsync(
        ObsEventReceivedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty || string.IsNullOrEmpty(@event.ObsEventType))
            return;

        Dictionary<string, string> variables = BuildVariables(@event);
        try
        {
            await _executor.ExecuteAsync(
                @event.BroadcasterId,
                $"obs.{@event.ObsEventType}",
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
                "obs_event trigger dispatch failed for {Channel} ({EventType}).",
                @event.BroadcasterId,
                @event.ObsEventType
            );
        }
    }

    /// <summary>Flat event fields → <c>obs.event.&lt;name&gt;</c>; nested objects ride as raw JSON.</summary>
    internal static Dictionary<string, string> BuildVariables(ObsEventReceivedEvent e)
    {
        Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase)
        {
            ["obs.event.type"] = e.ObsEventType,
        };
        try
        {
            using JsonDocument doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(e.DataJson) ? "{}" : e.DataJson
            );
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in doc.RootElement.EnumerateObject())
                    variables[$"obs.event.{property.Name}"] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                        JsonValueKind.Null => string.Empty,
                        _ => property.Value.ToString(),
                    };
            }
        }
        catch (JsonException)
        {
            // A malformed payload still fires the trigger with the type var alone.
        }
        return variables;
    }
}
