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
using NomNomzBot.Application.Abstractions.Persistence;
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

/// <summary>Broadcasts a prediction opening (<c>channel.prediction.begin</c>) to dashboard clients AND,
/// identically, to overlay widgets + the feed (the <c>poll_prediction</c> widget binds <c>prediction_begin</c>).</summary>
public sealed class PredictionBeganBroadcastHandler : IEventHandler<PredictionBeganEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;

    public PredictionBeganBroadcastHandler(
        IDashboardNotifier notifier,
        IApplicationDbContext db,
        IWidgetNotifier widgets
    )
    {
        _notifier = notifier;
        _db = db;
        _widgets = widgets;
    }

    public async Task HandleAsync(PredictionBeganEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        PredictionBeganAlertDto dto = new(
            @event.PredictionId,
            @event.Title,
            PredictionAlertMapper.MapOutcomes(@event.Outcomes),
            @event.WindowSeconds,
            @event.LocksAt
        );

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "prediction_begin",
            dto,
            ct
        );

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            @event.BroadcasterId,
            "prediction_begin",
            dto,
            ct
        );
    }
}

/// <summary>Broadcasts a running prediction's pools (<c>channel.prediction.progress</c>) to dashboard clients
/// AND, identically, to overlay widgets + the feed.</summary>
public sealed class PredictionProgressBroadcastHandler : IEventHandler<PredictionProgressEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;

    public PredictionProgressBroadcastHandler(
        IDashboardNotifier notifier,
        IApplicationDbContext db,
        IWidgetNotifier widgets
    )
    {
        _notifier = notifier;
        _db = db;
        _widgets = widgets;
    }

    public async Task HandleAsync(PredictionProgressEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        PredictionProgressAlertDto dto = new(
            @event.PredictionId,
            @event.Title,
            PredictionAlertMapper.MapOutcomes(@event.Outcomes),
            @event.LocksAt
        );

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "prediction_progress",
            dto,
            ct
        );

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            @event.BroadcasterId,
            "prediction_progress",
            dto,
            ct
        );
    }
}

/// <summary>Broadcasts a prediction's vote lock (<c>channel.prediction.lock</c>) to dashboard clients AND,
/// identically, to overlay widgets + the feed.</summary>
public sealed class PredictionLockedBroadcastHandler : IEventHandler<PredictionLockedEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;

    public PredictionLockedBroadcastHandler(
        IDashboardNotifier notifier,
        IApplicationDbContext db,
        IWidgetNotifier widgets
    )
    {
        _notifier = notifier;
        _db = db;
        _widgets = widgets;
    }

    public async Task HandleAsync(PredictionLockedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        PredictionLockedAlertDto dto = new(
            @event.PredictionId,
            @event.Title,
            PredictionAlertMapper.MapOutcomes(@event.Outcomes)
        );

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "prediction_lock",
            dto,
            ct
        );

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            @event.BroadcasterId,
            "prediction_lock",
            dto,
            ct
        );
    }
}

/// <summary>Broadcasts a prediction's resolution/cancellation (<c>channel.prediction.end</c>) to dashboard
/// clients AND, identically, to overlay widgets + the feed.</summary>
public sealed class PredictionEndedBroadcastHandler : IEventHandler<PredictionEndedEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;

    public PredictionEndedBroadcastHandler(
        IDashboardNotifier notifier,
        IApplicationDbContext db,
        IWidgetNotifier widgets
    )
    {
        _notifier = notifier;
        _db = db;
        _widgets = widgets;
    }

    public async Task HandleAsync(PredictionEndedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        PredictionEndedAlertDto dto = new(
            @event.PredictionId,
            @event.Title,
            @event.Status,
            PredictionAlertMapper.MapOutcomes(@event.Outcomes),
            @event.WinningOutcomeId
        );

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "prediction_end",
            dto,
            ct
        );

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            @event.BroadcasterId,
            "prediction_end",
            dto,
            ct
        );
    }
}
