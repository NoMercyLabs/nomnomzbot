// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Widgets.EventHandlers;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// Gives `goal_bar` its real value after a reload, as one <c>goal</c> frame in the shape
/// <see cref="GoalWidgetEventHandler"/> sends live. Followers and subs come from the active Twitch creator goal
/// whose metric matches the widget's <c>metric</c> setting. Twitch has no bits goal, so a bits goal is the sum of
/// this channel's own stored cheers since the widget's <c>startDate</c>. A failed call, no matching goal, or a bits
/// goal without a start date or target gives no frame, so the bar keeps its idle state instead of a 0.
/// </summary>
public sealed class GoalSeedProvider(ITwitchGoalsApi goals, IApplicationDbContext db)
    : IWidgetSeedProvider
{
    private const string DefaultMetric = "followers";
    private const string BitsMetric = "bits";
    private const string CheerEventType = "channel.cheer";

    public string NaturalKey => "goal_bar";

    public async Task<IReadOnlyList<WidgetSeedFrame>> SeedAsync(
        Guid broadcasterId,
        Widget widget,
        CancellationToken cancellationToken
    )
    {
        string metric = ReadText(widget, "metric") ?? DefaultMetric;
        if (metric == BitsMetric)
            return await SeedBitsAsync(broadcasterId, widget, cancellationToken);

        Result<IReadOnlyList<TwitchCreatorGoal>> result = await goals.GetCreatorGoalsAsync(
            broadcasterId,
            cancellationToken
        );
        if (result.IsFailure)
            return [];

        foreach (TwitchCreatorGoal goal in result.Value)
        {
            if (GoalMetrics.FromTwitchGoalType(goal.Type) != metric)
                continue;

            return [Frame(metric, goal.CurrentAmount, goal.TargetAmount)];
        }

        return [];
    }

    private async Task<IReadOnlyList<WidgetSeedFrame>> SeedBitsAsync(
        Guid broadcasterId,
        Widget widget,
        CancellationToken cancellationToken
    )
    {
        if (
            !DateTimeOffset.TryParse(
                ReadText(widget, "startDate"),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out DateTimeOffset start
            )
            || !int.TryParse(
                ReadText(widget, "target"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int target
            )
        )
            return [];

        // CreatedAt is the write time, never earlier than the cheer itself, so it only narrows the read. The
        // cheer's true moment is the occurredAt kept in Data (a legacy import rewrites CreatedAt).
        DateTime writtenFrom = start.UtcDateTime;
        List<ChannelEvent> cheers = await db
            .ChannelEvents.AsNoTracking()
            .Where(e =>
                e.ChannelId == broadcasterId
                && e.Type == CheerEventType
                && e.CreatedAt >= writtenFrom
            )
            .ToListAsync(cancellationToken);

        long bits = 0;
        foreach (ChannelEvent cheer in cheers)
        {
            (int cheerBits, DateTimeOffset? occurredAt) = ReadCheer(cheer.Data);
            DateTimeOffset when =
                occurredAt
                ?? new DateTimeOffset(DateTime.SpecifyKind(cheer.CreatedAt, DateTimeKind.Utc));
            if (when >= start)
                bits += cheerBits;
        }

        return [Frame(BitsMetric, (int)Math.Min(bits, int.MaxValue), target)];
    }

    private static WidgetSeedFrame Frame(string metric, int value, int target) =>
        new("goal", new GoalWidgetEventPayload(metric, value, target), DateTimeOffset.UtcNow);

    private static (int Bits, DateTimeOffset? OccurredAt) ReadCheer(string? data)
    {
        if (string.IsNullOrEmpty(data))
            return (0, null);

        try
        {
            using JsonDocument document = JsonDocument.Parse(data);
            JsonElement root = document.RootElement;
            int bits =
                root.TryGetProperty("bits", out JsonElement bitsElement)
                && bitsElement.TryGetInt32(out int parsed)
                    ? parsed
                    : 0;
            DateTimeOffset? occurredAt =
                root.TryGetProperty("occurredAt", out JsonElement atElement)
                && atElement.TryGetDateTimeOffset(out DateTimeOffset at)
                    ? at
                    : null;
            return (bits, occurredAt);
        }
        catch (JsonException)
        {
            return (0, null);
        }
    }

    // Settings come back from the JSON column as a string, a number or a JsonElement.
    private static string? ReadText(Widget widget, string key)
    {
        if (!widget.Settings.TryGetValue(key, out object? raw))
            return null;

        string? text = raw switch
        {
            string value => value,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonElement { ValueKind: JsonValueKind.Number } element => element.GetRawText(),
            null => null,
            _ => Convert.ToString(raw, CultureInfo.InvariantCulture),
        };
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
