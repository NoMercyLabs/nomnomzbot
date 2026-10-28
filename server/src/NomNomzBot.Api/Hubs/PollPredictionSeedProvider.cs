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
