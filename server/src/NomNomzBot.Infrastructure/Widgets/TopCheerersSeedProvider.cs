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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// Gives `top_cheerers` its board back after a reload: one <c>cheer</c> frame per cheerer carrying the bits summed
/// from this channel's own stored <c>channel.cheer</c> rows, in the shape the widget already adds up live
/// (<c>displayName</c> and <c>bits</c>). The widget's <c>range</c> setting picks the window: <c>stream</c> counts from
/// the moment the channel went live (offline gives no frames, the widget shows its empty state), <c>sinceReset</c>
/// counts from the <c>resetAt</c> setting (empty means all time).
/// </summary>
internal sealed class TopCheerersSeedProvider(IApplicationDbContext db, IChannelRegistry registry)
    : IWidgetSeedProvider
{
    private const string SinceResetRange = "sinceReset";

    public string NaturalKey => "top_cheerers";

    public async Task<IReadOnlyList<WidgetSeedFrame>> SeedAsync(
        Guid broadcasterId,
        Widget widget,
        CancellationToken cancellationToken
    )
    {
        bool sinceReset = WidgetSettingText.Read(widget, "range") == SinceResetRange;
        DateTimeOffset? from = sinceReset
            ? ResetMoment(widget)
            : registry.Get(broadcasterId)?.WentLiveAt;
        if (!sinceReset && from is null)
            return [];

        IReadOnlyList<CheerSeedPayload> board = await CheerBoard.LoadAsync(
            db,
            broadcasterId,
            from,
            cancellationToken
        );
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return [.. board.Select(c => new WidgetSeedFrame("cheer", c, now))];
    }

    private static DateTimeOffset? ResetMoment(Widget widget) =>
        DateTimeOffset.TryParse(
            WidgetSettingText.Read(widget, "resetAt"),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out DateTimeOffset reset
        )
            ? reset
            : null;
}
