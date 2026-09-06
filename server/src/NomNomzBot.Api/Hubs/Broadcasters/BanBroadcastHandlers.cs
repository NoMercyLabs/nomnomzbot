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
using NomNomzBot.Application.Alerts.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts user ban events to the dashboard AND, identically, to overlay widgets + the feed.</summary>
public sealed class UserBannedBroadcastHandler : IEventHandler<UserBannedEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IHubUserEnricher _enricher;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;
    private readonly IAlertQueueService _alertQueue;
    private readonly IWidgetService _widgetService;

    public UserBannedBroadcastHandler(
        IDashboardNotifier notifier,
        IHubUserEnricher enricher,
        IApplicationDbContext db,
        IWidgetNotifier widgets,
        IAlertQueueService alertQueue,
        IWidgetService widgetService
    )
    {
        _notifier = notifier;
        _enricher = enricher;
        _db = db;
        _widgets = widgets;
        _alertQueue = alertQueue;
        _widgetService = widgetService;
    }

    public async Task HandleAsync(UserBannedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        HubUserEnrichment? enrichment = await _enricher.EnrichAsync(
            @event.BroadcasterId,
            @event.TargetUserId,
            ct
        );

        ModActionDto dto = new(
            "ban",
            @event.ModeratorUserId,
            @event.TargetUserId,
            @event.Reason,
            null,
            enrichment?.DisplayName,
            enrichment?.AvatarUrl,
            enrichment?.Pronouns,
            enrichment?.CommunityStanding,
            @event.ModeratorDisplayName,
            @event.OccurredAt
        );

        await _notifier.SendModActionAsync(@event.BroadcasterId.ToString(), dto, ct);

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            _alertQueue,
            _widgetService,
            @event.BroadcasterId,
            @event.Provider,
            "ban",
            dto,
            @event.EventId.ToString(),
            ct
        );
    }
}

/// <summary>Broadcasts user timeout events to the dashboard AND, identically, to overlay widgets + the feed.</summary>
public sealed class UserTimedOutBroadcastHandler : IEventHandler<UserTimedOutEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IHubUserEnricher _enricher;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;
    private readonly IAlertQueueService _alertQueue;
    private readonly IWidgetService _widgetService;

    public UserTimedOutBroadcastHandler(
        IDashboardNotifier notifier,
        IHubUserEnricher enricher,
        IApplicationDbContext db,
        IWidgetNotifier widgets,
        IAlertQueueService alertQueue,
        IWidgetService widgetService
    )
    {
        _notifier = notifier;
        _enricher = enricher;
        _db = db;
        _widgets = widgets;
        _alertQueue = alertQueue;
        _widgetService = widgetService;
    }

    public async Task HandleAsync(UserTimedOutEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        HubUserEnrichment? enrichment = await _enricher.EnrichAsync(
            @event.BroadcasterId,
            @event.TargetUserId,
            ct
        );

        ModActionDto dto = new(
            "timeout",
            @event.ModeratorUserId,
            @event.TargetUserId,
            @event.Reason,
            @event.DurationSeconds,
            enrichment?.DisplayName,
            enrichment?.AvatarUrl,
            enrichment?.Pronouns,
            enrichment?.CommunityStanding,
            @event.ModeratorDisplayName,
            @event.OccurredAt
        );

        await _notifier.SendModActionAsync(@event.BroadcasterId.ToString(), dto, ct);

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            _alertQueue,
            _widgetService,
            @event.BroadcasterId,
            @event.Provider,
            "timeout",
            dto,
            @event.EventId.ToString(),
            ct
        );
    }
}

/// <summary>Broadcasts user unban events to the dashboard AND, identically, to overlay widgets + the feed.</summary>
public sealed class UserUnbannedBroadcastHandler : IEventHandler<UserUnbannedEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IHubUserEnricher _enricher;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;
    private readonly IAlertQueueService _alertQueue;
    private readonly IWidgetService _widgetService;

    public UserUnbannedBroadcastHandler(
        IDashboardNotifier notifier,
        IHubUserEnricher enricher,
        IApplicationDbContext db,
        IWidgetNotifier widgets,
        IAlertQueueService alertQueue,
        IWidgetService widgetService
    )
    {
        _notifier = notifier;
        _enricher = enricher;
        _db = db;
        _widgets = widgets;
        _alertQueue = alertQueue;
        _widgetService = widgetService;
    }

    public async Task HandleAsync(UserUnbannedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        HubUserEnrichment? enrichment = await _enricher.EnrichAsync(
            @event.BroadcasterId,
            @event.TargetUserId,
            ct
        );

        ModActionDto dto = new(
            "unban",
            @event.ModeratorUserId,
            @event.TargetUserId,
            null,
            null,
            enrichment?.DisplayName,
            enrichment?.AvatarUrl,
            enrichment?.Pronouns,
            enrichment?.CommunityStanding,
            @event.ModeratorDisplayName,
            @event.OccurredAt
        );

        await _notifier.SendModActionAsync(@event.BroadcasterId.ToString(), dto, ct);

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            _alertQueue,
            _widgetService,
            @event.BroadcasterId,
            NomNomzBot.Domain.Identity.Enums.AuthEnums.Platform.Twitch,
            "unban",
            dto,
            @event.EventId.ToString(),
            ct
        );
    }
}
