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

/// <summary>
/// Maps the shared hype train contribution shape onto its hub DTO — reused by the broadcasters below. The full DTO
/// (level/progress/goal PLUS the top-contribution list and expiry) is what both the dashboard and the overlays now
/// receive: the persistent hype-train overlay meter no longer gets a thinner flattened payload than the dashboard.
/// </summary>
internal static class HypeTrainAlertMapper
{
    public static IReadOnlyList<HypeTrainContributionDto> MapContributions(
        IReadOnlyList<HypeTrainContribution> contributions
    ) =>
        contributions
            .Select(c => new HypeTrainContributionDto(
                c.UserId,
                c.UserLogin,
                c.UserDisplayName,
                c.Type,
                c.Total
            ))
            .ToList();
}

/// <summary>Broadcasts a hype train starting (<c>channel.hype_train.begin</c>) to the dashboard AND the overlay meter.</summary>
public sealed class HypeTrainBeganBroadcastHandler : IEventHandler<HypeTrainBeganEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;

    public HypeTrainBeganBroadcastHandler(
        IDashboardNotifier notifier,
        IApplicationDbContext db,
        IWidgetNotifier widgets
    )
    {
        _notifier = notifier;
        _db = db;
        _widgets = widgets;
    }

    public async Task HandleAsync(HypeTrainBeganEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        HypeTrainBeganAlertDto dto = new(
            @event.HypeTrainId,
            @event.Level,
            @event.Total,
            @event.Progress,
            @event.Goal,
            HypeTrainAlertMapper.MapContributions(@event.TopContributions),
            @event.ExpiresAt
        );

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "hype_train_begin",
            dto,
            ct
        );

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            @event.BroadcasterId,
            "hype_train_begin",
            dto,
            ct
        );
    }
}

/// <summary>Broadcasts a hype train progress tick (<c>channel.hype_train.progress</c>) to the dashboard AND the overlay meter.</summary>
public sealed class HypeTrainProgressBroadcastHandler : IEventHandler<HypeTrainProgressEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;

    public HypeTrainProgressBroadcastHandler(
        IDashboardNotifier notifier,
        IApplicationDbContext db,
        IWidgetNotifier widgets
    )
    {
        _notifier = notifier;
        _db = db;
        _widgets = widgets;
    }

    public async Task HandleAsync(HypeTrainProgressEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        HypeTrainProgressAlertDto dto = new(
            @event.HypeTrainId,
            @event.Level,
            @event.Total,
            @event.Progress,
            @event.Goal,
            HypeTrainAlertMapper.MapContributions(@event.TopContributions),
            @event.ExpiresAt
        );

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "hype_train_progress",
            dto,
            ct
        );

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            @event.BroadcasterId,
            "hype_train_progress",
            dto,
            ct
        );
    }
}

/// <summary>Broadcasts a hype train's final level (<c>channel.hype_train.end</c>) to the dashboard AND the overlay meter.</summary>
public sealed class HypeTrainEndedBroadcastHandler : IEventHandler<HypeTrainEndedEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;

    public HypeTrainEndedBroadcastHandler(
        IDashboardNotifier notifier,
        IApplicationDbContext db,
        IWidgetNotifier widgets
    )
    {
        _notifier = notifier;
        _db = db;
        _widgets = widgets;
    }

    public async Task HandleAsync(HypeTrainEndedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        HypeTrainEndedAlertDto dto = new(
            @event.HypeTrainId,
            @event.Level,
            @event.Total,
            HypeTrainAlertMapper.MapContributions(@event.TopContributions),
            @event.EndedAt
        );

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "hype_train_end",
            dto,
            ct
        );

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            @event.BroadcasterId,
            "hype_train_end",
            dto,
            ct
        );
    }
}
