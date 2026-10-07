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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Widgets.EventHandlers;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// Gives `goal_bar` its real value after a reload: the active Twitch creator goal whose metric matches the
/// widget's <c>metric</c> setting, as one <c>goal</c> frame in the shape <see cref="GoalWidgetEventHandler"/>
/// sends live. A failed call or no matching goal gives no frame, so the bar keeps its idle state instead of a 0.
/// </summary>
public sealed class GoalSeedProvider(ITwitchGoalsApi goals) : IWidgetSeedProvider
{
    private const string DefaultMetric = "followers";

    public string NaturalKey => "goal_bar";

    public async Task<IReadOnlyList<WidgetSeedFrame>> SeedAsync(
        Guid broadcasterId,
        Widget widget,
        CancellationToken cancellationToken
    )
    {
        Result<IReadOnlyList<TwitchCreatorGoal>> result = await goals.GetCreatorGoalsAsync(
            broadcasterId,
            cancellationToken
        );
        if (result.IsFailure)
            return [];

        string metric = ReadMetric(widget);
        foreach (TwitchCreatorGoal goal in result.Value)
        {
            if (GoalMetrics.FromTwitchGoalType(goal.Type) != metric)
                continue;

            return
            [
                new WidgetSeedFrame(
                    "goal",
                    new GoalWidgetEventPayload(metric, goal.CurrentAmount, goal.TargetAmount),
                    DateTimeOffset.UtcNow
                ),
            ];
        }

        return [];
    }

    // Settings come back from the JSON column as a string or a JsonElement.
    private static string ReadMetric(Widget widget)
    {
        if (!widget.Settings.TryGetValue("metric", out object? raw))
            return DefaultMetric;

        string? metric = raw switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(metric) ? DefaultMetric : metric;
    }
}
