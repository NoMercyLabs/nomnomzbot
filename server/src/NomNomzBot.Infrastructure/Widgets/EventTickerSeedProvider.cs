// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// Gives the event_ticker widget its last chips on join, read from the alert captures kept beside
/// <see cref="RenderedAlertCaptureLog"/>. It lives in Infrastructure/Widgets because the capture store and its data
/// access (<see cref="IApplicationDbContext"/>) live here. A frame is the live event of the same type with the
/// payload exactly as it was pushed.
/// </summary>
internal sealed class EventTickerSeedProvider(IApplicationDbContext db) : IWidgetSeedProvider
{
    // The defaults event_ticker.vue starts with when its settings carry no events or count.
    private static readonly string[] DefaultEvents =
    [
        "follow",
        "subscription",
        "resub",
        "gift",
        "cheer",
        "raid",
        "supporter.tip",
        "supporter.membership",
        "supporter.merch",
        "supporter.charity",
    ];
    private const int DefaultCount = 20;

    public string NaturalKey => "event_ticker";

    public async Task<IReadOnlyList<WidgetSeedFrame>> SeedAsync(
        Guid broadcasterId,
        Widget widget,
        CancellationToken cancellationToken
    )
    {
        string[] shownTypes = ShownTypes(widget.Settings);
        int count = Count(widget.Settings);

        List<RenderedAlertCapture> newest = await db
            .RenderedAlertCaptures.AsNoTracking()
            .Where(c => c.BroadcasterId == broadcasterId && shownTypes.Contains(c.EventType))
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Take(count)
            .ToListAsync(cancellationToken);

        newest.Reverse();
        return
        [
            .. newest.Select(c => new WidgetSeedFrame(
                c.EventType,
                JsonSerializer.Deserialize<JsonElement>(c.Payload),
                new DateTimeOffset(DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc))
            )),
        ];
    }

    private static string[] ShownTypes(Dictionary<string, object> settings)
    {
        if (
            !settings.TryGetValue("events", out object? raw)
            || raw is not IEnumerable items
            || raw is string
        )
            return DefaultEvents;

        return [.. items.Cast<object?>().Select(i => i?.ToString()).OfType<string>()];
    }

    private static int Count(Dictionary<string, object> settings)
    {
        if (
            settings.TryGetValue("count", out object? raw)
            && int.TryParse(
                Convert.ToString(raw, CultureInfo.InvariantCulture),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int count
            )
            && count > 0
        )
            return count;

        return DefaultCount;
    }
}
