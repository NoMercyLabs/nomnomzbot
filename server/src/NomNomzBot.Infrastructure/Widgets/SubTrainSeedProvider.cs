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
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// Gives `sub_train` its train back after a reload: the subs still inside the widget's <c>windowMs</c>, read from the
/// channel-event log and replayed as the events the widget already listens to, each stamped with when it really
/// happened so the widget can work out the time left. A gift counts once, by its gift count; the gifted subs it
/// produced (<c>channel.subscribe</c> with <c>isGift</c>) and the received rows are skipped, never counted again.
/// </summary>
internal sealed class SubTrainSeedProvider(IApplicationDbContext db, TimeProvider clock)
    : IWidgetSeedProvider
{
    private const int DefaultWindowMs = 300000;

    private static readonly Dictionary<string, string> FrameTypeByEventType = new(
        StringComparer.Ordinal
    )
    {
        ["channel.subscribe"] = "subscription",
        ["channel.subscription.message"] = "resub",
        ["channel.subscription.gift"] = "gift",
    };

    public string NaturalKey => "sub_train";

    public async Task<IReadOnlyList<WidgetSeedFrame>> SeedAsync(
        Guid broadcasterId,
        Widget widget,
        CancellationToken cancellationToken
    )
    {
        DateTime cutoff =
            clock.GetUtcNow().UtcDateTime - TimeSpan.FromMilliseconds(WindowMs(widget));
        string[] types = [.. FrameTypeByEventType.Keys];

        List<ChannelEvent> rows = await db
            .ChannelEvents.AsNoTracking()
            .Where(e =>
                e.ChannelId == broadcasterId && types.Contains(e.Type) && e.CreatedAt >= cutoff
            )
            .ToListAsync(cancellationToken);

        List<WidgetSeedFrame> frames = [];
        foreach (ChannelEvent row in rows)
        {
            WidgetSeedFrame? frame = ToFrame(row, cutoff);
            if (frame is not null)
                frames.Add(frame);
        }

        return [.. frames.OrderBy(f => f.OccurredAt)];
    }

    private static WidgetSeedFrame? ToFrame(ChannelEvent row, DateTime cutoff)
    {
        JsonElement data = ParseData(row.Data);
        if (row.Type == "channel.subscribe" && ReadBool(data, "isGift"))
            return null;

        DateTime occurredAt =
            ReadOccurredAt(data) ?? DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc);
        if (occurredAt < cutoff)
            return null;

        string frameType = FrameTypeByEventType[row.Type];
        object? payload =
            frameType == "gift"
                ? new SubTrainGiftPayload(Math.Max(1, ReadInt(data, "giftCount")))
                : null;
        return new WidgetSeedFrame(frameType, payload, new DateTimeOffset(occurredAt));
    }

    private static JsonElement ParseData(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static bool ReadBool(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.True;

    private static int ReadInt(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out int number)
            ? number
            : 0;

    private static DateTime? ReadOccurredAt(JsonElement data)
    {
        if (
            data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("occurredAt", out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            && DateTime.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out DateTime parsed
            )
        )
            return parsed;

        return null;
    }

    private static int WindowMs(Widget widget)
    {
        if (
            widget.Settings.TryGetValue("windowMs", out object? raw)
            && int.TryParse(
                Convert.ToString(raw, CultureInfo.InvariantCulture),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int windowMs
            )
            && windowMs > 0
        )
            return windowMs;

        return DefaultWindowMs;
    }

    /// <summary>The <c>gift</c> event data the widget reads: a gift of N counts as N.</summary>
    private sealed record SubTrainGiftPayload([property: JsonPropertyName("count")] int Count);
}
