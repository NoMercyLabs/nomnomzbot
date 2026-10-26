// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// Gives poll_prediction the running poll and prediction back on join: the newest frame of each. A finished one
/// is replayed only while the widget would still show its result (it hides 8 seconds after the end frame).
/// </summary>
public sealed class PollPredictionSeedProvider(PollPredictionStateStore store, TimeProvider clock)
    : IWidgetSeedProvider
{
    private static readonly TimeSpan ResultShownFor = TimeSpan.FromSeconds(8); // poll_prediction.vue scheduleHide

    public string NaturalKey => "poll_prediction";

    public Task<IReadOnlyList<WidgetSeedFrame>> SeedAsync(
        Guid broadcasterId,
        Widget widget,
        CancellationToken cancellationToken
    )
    {
        DateTimeOffset now = clock.GetUtcNow();
        IReadOnlyList<WidgetSeedFrame> frames =
        [
            .. store
                .Latest(broadcasterId)
                .Where(f => !f.EventType.EndsWith("_end") || now - f.OccurredAt < ResultShownFor),
        ];
        return Task.FromResult(frames);
    }
}
