// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Maps the shared prediction-outcome shape onto its hub DTO — reused by the broadcasters below.</summary>
internal static class PredictionAlertMapper
{
    public static IReadOnlyList<PredictionOutcomeDto> MapOutcomes(
        IReadOnlyList<PredictionOutcome> outcomes
    ) =>
        outcomes
            .Select(o => new PredictionOutcomeDto(o.Id, o.Title, o.ChannelPoints, o.Users, o.Color))
            .ToList();
}

/// <summary>Broadcasts a prediction opening (<c>channel.prediction.begin</c>) to dashboard clients.</summary>
public sealed class PredictionBeganBroadcastHandler : IEventHandler<PredictionBeganEvent>
{
    private readonly IDashboardNotifier _notifier;

    public PredictionBeganBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(PredictionBeganEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "prediction_begin",
            new PredictionBeganAlertDto(
                @event.PredictionId,
                @event.Title,
                PredictionAlertMapper.MapOutcomes(@event.Outcomes),
                @event.WindowSeconds,
                @event.LocksAt
            ),
            ct
        );
    }
}

/// <summary>Broadcasts a running prediction's pools (<c>channel.prediction.progress</c>) to dashboard clients.</summary>
public sealed class PredictionProgressBroadcastHandler : IEventHandler<PredictionProgressEvent>
{
    private readonly IDashboardNotifier _notifier;

    public PredictionProgressBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(PredictionProgressEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "prediction_progress",
            new PredictionProgressAlertDto(
                @event.PredictionId,
                @event.Title,
                PredictionAlertMapper.MapOutcomes(@event.Outcomes),
                @event.LocksAt
            ),
            ct
        );
    }
}

/// <summary>Broadcasts a prediction's vote lock (<c>channel.prediction.lock</c>) to dashboard clients.</summary>
public sealed class PredictionLockedBroadcastHandler : IEventHandler<PredictionLockedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public PredictionLockedBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(PredictionLockedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "prediction_lock",
            new PredictionLockedAlertDto(
                @event.PredictionId,
                @event.Title,
                PredictionAlertMapper.MapOutcomes(@event.Outcomes)
            ),
            ct
        );
    }
}

/// <summary>Broadcasts a prediction's resolution/cancellation (<c>channel.prediction.end</c>) to dashboard clients.</summary>
public sealed class PredictionEndedBroadcastHandler : IEventHandler<PredictionEndedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public PredictionEndedBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(PredictionEndedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "prediction_end",
            new PredictionEndedAlertDto(
                @event.PredictionId,
                @event.Title,
                @event.Status,
                PredictionAlertMapper.MapOutcomes(@event.Outcomes),
                @event.WinningOutcomeId
            ),
            ct
        );
    }
}
