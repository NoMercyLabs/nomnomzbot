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

using System.Collections.Concurrent;
using NomNomzBot.Application.Widgets.Dtos;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// The newest poll frame and the newest prediction frame each channel pushed, exactly as the overlay got them,
/// so a poll_prediction page that loads mid-poll can be handed the running poll. In memory: a restart forgets
/// it, and the next Twitch progress tick fills it again.
/// </summary>
public sealed class PollPredictionStateStore(TimeProvider clock)
{
    public const string PollTopic = "poll";
    public const string PredictionTopic = "prediction";

    private readonly ConcurrentDictionary<(Guid Channel, string Topic), WidgetSeedFrame> _latest =
        new();

    public void Remember(Guid broadcasterId, string topic, string eventType, object payload) =>
        _latest[(broadcasterId, topic)] = new(eventType, payload, clock.GetUtcNow());

    public IReadOnlyList<WidgetSeedFrame> Latest(Guid broadcasterId) =>
        [
            .. new[] { PollTopic, PredictionTopic }
                .Select(topic =>
                    _latest.TryGetValue((broadcasterId, topic), out WidgetSeedFrame? frame)
                        ? frame
                        : null
                )
                .OfType<WidgetSeedFrame>(),
        ];
}
