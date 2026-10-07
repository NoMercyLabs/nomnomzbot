// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.CustomEvents;

/// <summary>
/// Gives the custom_data widget its last value on join, read from the latest-value cache the ingest service
/// fills. The frame is the live <c>custom.&lt;source&gt;</c> event with the same <c>{ fields }</c> payload.
/// </summary>
internal sealed class CustomDataSeedProvider(ICacheService cache) : IWidgetSeedProvider
{
    public string NaturalKey => "custom_data";

    public async Task<IReadOnlyList<WidgetSeedFrame>> SeedAsync(
        Guid broadcasterId,
        Widget widget,
        CancellationToken cancellationToken
    )
    {
        string? source = widget.Settings.GetValueOrDefault("source")?.ToString();
        if (string.IsNullOrWhiteSpace(source))
            return [];

        CustomDataLatestValue? latest = await cache.GetAsync<CustomDataLatestValue>(
            $"customdata:{broadcasterId}:{source}",
            cancellationToken
        );
        if (latest is null)
            return [];

        return
        [
            new WidgetSeedFrame(
                $"custom.{source}",
                new { fields = latest.Fields },
                new DateTimeOffset(DateTime.SpecifyKind(latest.ReceivedAt, DateTimeKind.Utc))
            ),
        ];
    }
}
