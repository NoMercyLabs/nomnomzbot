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

using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Keeps the newest poll and prediction frame per channel in <see cref="PollPredictionStateStore"/>, built by
/// the same mappers the broadcast handlers push, so a reloaded overlay gets exactly what it would have seen.
/// </summary>
public sealed class PollPredictionStateRecorder(PollPredictionStateStore store)
    : IEventHandler<PollBeganEvent>,
        IEventHandler<PollProgressEvent>,
        IEventHandler<PollEndedEvent>,
        IEventHandler<PredictionBeganEvent>,
        IEventHandler<PredictionProgressEvent>,
        IEventHandler<PredictionLockedEvent>,
        IEventHandler<PredictionEndedEvent>
{
    private const string Poll = PollPredictionStateStore.PollTopic;
    private const string Prediction = PollPredictionStateStore.PredictionTopic;

    public Task HandleAsync(PollBeganEvent @event, CancellationToken ct = default) =>
        Remember(@event.BroadcasterId, Poll, "poll_begin", PollAlertMapper.Began(@event));

    public Task HandleAsync(PollProgressEvent @event, CancellationToken ct = default) =>
        Remember(@event.BroadcasterId, Poll, "poll_progress", PollAlertMapper.Progress(@event));

    public Task HandleAsync(PollEndedEvent @event, CancellationToken ct = default) =>
        Remember(@event.BroadcasterId, Poll, "poll_end", PollAlertMapper.Ended(@event));

    public Task HandleAsync(PredictionBeganEvent @event, CancellationToken ct = default) =>
        Remember(
            @event.BroadcasterId,
            Prediction,
            "prediction_begin",
            PredictionAlertMapper.Began(@event)
        );

    public Task HandleAsync(PredictionProgressEvent @event, CancellationToken ct = default) =>
        Remember(
            @event.BroadcasterId,
            Prediction,
            "prediction_progress",
            PredictionAlertMapper.Progress(@event)
        );

    public Task HandleAsync(PredictionLockedEvent @event, CancellationToken ct = default) =>
        Remember(
            @event.BroadcasterId,
            Prediction,
            "prediction_lock",
            PredictionAlertMapper.Locked(@event)
        );

    public Task HandleAsync(PredictionEndedEvent @event, CancellationToken ct = default) =>
        Remember(
            @event.BroadcasterId,
            Prediction,
            "prediction_end",
            PredictionAlertMapper.Ended(@event)
        );

    private Task Remember(Guid broadcasterId, string topic, string eventType, object payload)
    {
        if (broadcasterId != Guid.Empty)
            store.Remember(broadcasterId, topic, eventType, payload);
        return Task.CompletedTask;
    }
}
