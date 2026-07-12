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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Broadcasts an outgoing shoutout (<c>channel.shoutout.create</c> — this channel shouted another broadcaster
/// out) to dashboard clients.
/// </summary>
public sealed class ShoutoutSentBroadcastHandler : IEventHandler<ShoutoutSentEvent>
{
    private readonly IDashboardNotifier _notifier;

    public ShoutoutSentBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(ShoutoutSentEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "shoutout_sent",
            new ShoutoutSentAlertDto(@event.ToUserId, @event.ToDisplayName),
            ct,
            userId: @event.ToUserId,
            userDisplayName: @event.ToDisplayName
        );
    }
}

/// <summary>
/// Broadcasts an incoming shoutout (<c>channel.shoutout.receive</c> — another broadcaster shouted this channel
/// out) to dashboard clients.
/// </summary>
public sealed class ShoutoutReceivedBroadcastHandler : IEventHandler<ShoutoutReceivedEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IHubUserEnricher _enricher;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;

    public ShoutoutReceivedBroadcastHandler(
        IDashboardNotifier notifier,
        IHubUserEnricher enricher,
        IApplicationDbContext db,
        IWidgetNotifier widgets
    )
    {
        _notifier = notifier;
        _enricher = enricher;
        _db = db;
        _widgets = widgets;
    }

    public async Task HandleAsync(ShoutoutReceivedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        HubUserEnrichment? enrichment = await _enricher.EnrichAsync(
            @event.BroadcasterId,
            @event.FromBroadcasterId,
            ct
        );

        ShoutoutReceivedAlertDto dto = new(
            @event.FromBroadcasterId,
            @event.FromBroadcasterDisplayName,
            @event.FromBroadcasterLogin,
            @event.ViewerCount,
            enrichment?.AvatarUrl,
            enrichment?.Pronouns,
            enrichment?.CommunityStanding
        );

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "shoutout_received",
            dto,
            ct,
            userId: @event.FromBroadcasterId,
            userDisplayName: @event.FromBroadcasterDisplayName
        );

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            @event.BroadcasterId,
            "shoutout_received",
            dto,
            ct
        );
    }
}
