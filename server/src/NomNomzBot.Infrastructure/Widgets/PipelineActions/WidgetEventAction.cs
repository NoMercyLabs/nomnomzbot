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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Infrastructure.Widgets.PipelineActions;

/// <summary>
/// Pipeline action <c>widget_event</c> (widgets-overlays.md §6). Pushes one widget event to the target
/// widget's overlay group via <see cref="IWidgetEventNotifier"/>. Fail-closed: the widget must exist AND be
/// enabled in the executing tenant, otherwise a typed failure (no push, no throw). Parameters:
/// <c>widget_id</c> (Guid, required), <c>event_type</c> (string, required), <c>data</c> (optional JSON
/// object the widget renders). Values are already template-resolved by the engine before the action runs;
/// the Vue overlay runtime escapes text interpolations by default, so string payloads render XSS-safe.
/// </summary>
public sealed class WidgetEventAction : ICommandAction
{
    private readonly IWidgetService _widgets;
    private readonly IWidgetEventNotifier _overlay;

    public string ActionType => "widget_event";

    public WidgetEventAction(IWidgetService widgets, IWidgetEventNotifier overlay)
    {
        _widgets = widgets;
        _overlay = overlay;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!Guid.TryParse(action.GetString("widget_id"), out Guid widgetId))
            return ActionResult.Failure("widget_event requires a 'widget_id' (GUID).");

        string? eventType = action.GetString("event_type");
        if (string.IsNullOrWhiteSpace(eventType))
            return ActionResult.Failure("widget_event requires an 'event_type'.");

        // Fail-closed: the widget must exist AND be enabled in THIS tenant before we push to its overlay.
        // GetAsync is tenant-scoped, so a widget owned by another channel resolves as not-found.
        Result<WidgetDetail> widget = await _widgets.GetAsync(
            ctx.BroadcasterId.ToString(),
            widgetId.ToString(),
            ctx.CancellationToken
        );
        if (!widget.IsSuccess)
            return ActionResult.Failure(
                $"widget_event: widget '{widgetId}' was not found in this channel."
            );
        if (!widget.Value.IsEnabled)
            return ActionResult.Failure($"widget_event: widget '{widgetId}' is disabled.");

        await _overlay.SendWidgetEventAsync(
            ctx.BroadcasterId,
            widgetId,
            eventType,
            ReadData(action),
            ctx.CancellationToken
        );

        return ActionResult.Success($"widget_event:{widgetId} type={eventType}");
    }

    // The optional 'data' param is an arbitrary JSON payload the widget renders. Materialize it to a plain CLR
    // graph (dictionaries / lists / primitives) so it round-trips through any hub protocol — a raw JsonElement
    // does not serialize cleanly over the MessagePack transport.
    private static object? ReadData(ActionDefinition action)
    {
        if (
            action.Parameters is null
            || !action.Parameters.TryGetValue("data", out JsonElement elem)
        )
            return null;
        return ToClr(elem);
    }

    private static object? ToClr(JsonElement e) =>
        e.ValueKind switch
        {
            JsonValueKind.Object => e.EnumerateObject()
                .ToDictionary(p => p.Name, p => ToClr(p.Value)),
            JsonValueKind.Array => e.EnumerateArray().Select(ToClr).ToList(),
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.TryGetInt64(out long l) ? l : e.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
}
