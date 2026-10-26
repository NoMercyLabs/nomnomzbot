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
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// Gives recent_followers its list back on join: the newest <c>count</c> follow alerts from the alert captures,
/// oldest first, because the widget puts each follow it receives on top.
/// </summary>
internal sealed class RecentFollowersSeedProvider(IApplicationDbContext db) : IWidgetSeedProvider
{
    private const string FollowEvent = "follow";
    private const int DefaultCount = 5; // recent_followers.vue starts with count 5

    public string NaturalKey => "recent_followers";

    public async Task<IReadOnlyList<WidgetSeedFrame>> SeedAsync(
        Guid broadcasterId,
        Widget widget,
        CancellationToken cancellationToken
    )
    {
        List<RenderedAlertCapture> newest = await db
            .RenderedAlertCaptures.AsNoTracking()
            .Where(c => c.BroadcasterId == broadcasterId && c.EventType == FollowEvent)
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Take(Count(widget))
            .ToListAsync(cancellationToken);

        newest.Reverse();
        return
        [
            .. newest.Select(c => new WidgetSeedFrame(
                FollowEvent,
                JsonSerializer.Deserialize<JsonElement>(c.Payload),
                new DateTimeOffset(DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc))
            )),
        ];
    }

    private static int Count(Widget widget) =>
        int.TryParse(
            WidgetSettingText.Read(widget, "count"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int count
        )
        && count > 0
            ? count
            : DefaultCount;
}
