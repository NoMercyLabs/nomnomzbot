// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
