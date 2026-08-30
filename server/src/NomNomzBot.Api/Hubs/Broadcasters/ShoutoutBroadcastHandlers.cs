// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Shared <c>ChannelEvents</c> logging for the shoutout-broadcast handlers in this file. Like VIP grants/
/// revocations (<c>RoleBroadcastChannelEventLogger</c>), shoutouts have no sibling handler deriving from
/// <c>TwitchAlertHandlerBase</c> to log the activity-feed row — these handlers ARE the only consumers of
/// <see cref="ShoutoutSentEvent"/>/<see cref="ShoutoutReceivedEvent"/>, so they own the write. Keyed by the
/// SAME domain-event <c>EventId</c> already threaded through the overlay alert-dispatch call as
/// <c>ChannelEventId</c>, so once this row exists that correlation resolves to something real instead of null.
/// Idempotent: an EventSub re-delivery skips rather than double-logs.
/// </summary>
internal static class ShoutoutBroadcastChannelEventLogger
{
    public static async Task LogAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        Guid eventId,
        string type,
        object data,
        CancellationToken ct
    )
    {
        string id = eventId.ToString();
        if (await db.ChannelEvents.AnyAsync(e => e.Id == id, ct))
            return;

        db.ChannelEvents.Add(
            new()
            {
                Id = id,
                ChannelId = broadcasterId,
                Type = type,
                Data = JsonSerializer.Serialize(data),
            }
        );
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Broadcasts an outgoing shoutout (<c>channel.shoutout.create</c> — this channel shouted another broadcaster
/// out) to dashboard clients.
/// </summary>
public sealed class ShoutoutSentBroadcastHandler : IEventHandler<ShoutoutSentEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;

    public ShoutoutSentBroadcastHandler(
        IDashboardNotifier notifier,
        IApplicationDbContext db,
        IWidgetNotifier widgets
    )
    {
        _notifier = notifier;
        _db = db;
        _widgets = widgets;
    }

    public async Task HandleAsync(ShoutoutSentEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        await ShoutoutBroadcastChannelEventLogger.LogAsync(
            _db,
            @event.BroadcasterId,
            @event.EventId,
            "channel.shoutout.create",
            new { toUserId = @event.ToUserId, toDisplayName = @event.ToDisplayName },
            ct
        );

        ShoutoutSentAlertDto dto = new(@event.ToUserId, @event.ToDisplayName);

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "shoutout_sent",
            dto,
            ct,
            userId: @event.ToUserId,
            userDisplayName: @event.ToDisplayName
        );

        // Mirrors ShoutoutReceivedBroadcastHandler below — an outgoing !so previously only reached the
        // dashboard toast, never the overlay, so no on-stream visual/audio alert widget could react to it.
        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            @event.BroadcasterId,
            "shoutout_sent",
            dto,
            @event.EventId.ToString(),
            ct
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

        await ShoutoutBroadcastChannelEventLogger.LogAsync(
            _db,
            @event.BroadcasterId,
            @event.EventId,
            "channel.shoutout.receive",
            new
            {
                fromBroadcasterId = @event.FromBroadcasterId,
                fromBroadcasterDisplayName = @event.FromBroadcasterDisplayName,
                viewerCount = @event.ViewerCount,
            },
            ct
        );

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
            @event.EventId.ToString(),
            ct
        );
    }
}
