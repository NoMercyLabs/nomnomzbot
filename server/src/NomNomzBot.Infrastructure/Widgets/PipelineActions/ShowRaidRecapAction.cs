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
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Infrastructure.Widgets.PipelineActions;

/// <summary>
/// Pipeline action <c>show_raid_recap</c> (raid-recap.md §7): requests the stats of the channel's latest stream
/// (the metric list from the widget's <c>metrics</c> setting, all metrics when unset) and sends them as one
/// <c>raid_recap.show</c> widget event to the channel's <c>raid_recap</c> system widget, the ending scene of a
/// raid. Fail-closed like <see cref="WidgetEventAction"/>: the widget must be enabled and open in a browser
/// source and the channel must have a stream, otherwise a typed failure (nothing sent, no throw). The widget
/// never computes numbers; it renders the <see cref="StreamStatsDto"/> it is sent.
/// </summary>
public sealed class ShowRaidRecapAction : ICommandAction
{
    private const string WidgetKey = "raid_recap";
    private const string EventType = "raid_recap.show";
    private const string MetricsSetting = "metrics";

    private readonly IStreamStatsService _stats;
    private readonly IWidgetService _widgets;
    private readonly IWidgetEventNotifier _overlay;

    public ShowRaidRecapAction(
        IStreamStatsService stats,
        IWidgetService widgets,
        IWidgetEventNotifier overlay
    )
    {
        _stats = stats;
        _widgets = widgets;
        _overlay = overlay;
    }

    public string ActionType => "show_raid_recap";

    public LocalizedText Category => new("pipeline.category.widgets");

    public LocalizedText Description => new("pipeline.show_raid_recap.description");

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        Result<WidgetDetail> widget = await _widgets.EnsureSystemWidgetAsync(
            ctx.BroadcasterId.ToString(),
            WidgetKey,
            ctx.CancellationToken
        );
        if (!widget.IsSuccess)
            return ActionResult.Failure(
                $"show_raid_recap: the '{WidgetKey}' widget could not be provisioned for this channel."
            );
        if (!widget.Value.IsEnabled)
            return ActionResult.Failure($"show_raid_recap: the '{WidgetKey}' widget is disabled.");
        if (!widget.Value.IsAttached)
            return ActionResult.Failure(
                $"show_raid_recap: no browser source has the '{WidgetKey}' widget open, so nobody would see the recap."
            );

        Result<StreamStatsDto> stats = await _stats.GetAsync(
            ctx.BroadcasterId,
            null,
            MetricsFrom(widget.Value.Settings),
            ctx.CancellationToken
        );
        if (!stats.IsSuccess)
            return ActionResult.Failure("show_raid_recap: this channel has no stream to recap.");

        await _overlay.SendWidgetEventAsync(
            ctx.BroadcasterId,
            widget.Value.Id,
            EventType,
            stats.Value,
            ctx.CancellationToken
        );

        return ActionResult.Success($"show_raid_recap:{widget.Value.Id}");
    }

    /// <summary>The metric keys of the widget's <c>metrics</c> setting (a JSON array, a list or a comma list), or
    /// null (all metrics) when the setting is absent or names no key.</summary>
    private static IReadOnlyCollection<string>? MetricsFrom(Dictionary<string, object?> settings)
    {
        if (!settings.TryGetValue(MetricsSetting, out object? raw) || raw is null)
            return null;

        IEnumerable<string?> items = raw switch
        {
            JsonElement { ValueKind: JsonValueKind.Array } array => array
                .EnumerateArray()
                .Select(e => e.GetString()),
            JsonElement { ValueKind: JsonValueKind.String } text => (
                text.GetString() ?? string.Empty
            ).Split(','),
            string text => text.Split(','),
            IEnumerable<object?> list => list.Select(o => o?.ToString()),
            _ => [],
        };

        List<string> keys = items
            .Select(k => k?.Trim())
            .Where(k => !string.IsNullOrEmpty(k))
            .Select(k => k!)
            .ToList();
        return keys.Count == 0 ? null : keys;
    }
}
