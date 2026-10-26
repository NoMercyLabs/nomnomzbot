// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
