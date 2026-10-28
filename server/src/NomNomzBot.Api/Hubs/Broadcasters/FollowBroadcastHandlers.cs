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

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Alerts.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts follow alerts to the dashboard AND, decorated identically, to overlay widgets + the feed.</summary>
public sealed class FollowBroadcastHandler : IEventHandler<FollowEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IHubUserEnricher _enricher;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;
    private readonly IAlertQueueService _alertQueue;
    private readonly IWidgetService _widgetService;

    public FollowBroadcastHandler(
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

    public async Task HandleAsync(FollowEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        HubUserEnrichment? enrichment = await _enricher.EnrichAsync(
            @event.BroadcasterId,
            @event.UserId,
            ct
        );

        FollowAlertDto dto = new(
            @event.UserId,
            @event.UserDisplayName,
            @event.UserLogin,
            @event.FollowedAt,
            enrichment?.AvatarUrl,
            enrichment?.Pronouns,
            enrichment?.CommunityStanding
        );

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "follow",
            dto,
            ct,
            userId: @event.UserId,
            userDisplayName: @event.UserDisplayName
        );

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            _alertQueue,
            _widgetService,
            @event.BroadcasterId,
            @event.Provider,
            "follow",
            dto,
            @event.EventId.ToString(),
            ct
        );
    }
}
