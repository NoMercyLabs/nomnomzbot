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
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Shared <c>ChannelEvents</c> logging for the role-broadcast handlers in this file. Unlike every other alert
/// type (follow/sub/cheer/raid/…), VIP grants/revocations have no sibling handler deriving from
/// <c>TwitchAlertHandlerBase</c> to log the activity-feed row — this IS the only consumer of
/// <see cref="VipAddedEvent"/>/<see cref="VipRemovedEvent"/>, so it owns the write. Keyed by the SAME domain-event
/// <c>EventId</c> the alert-dispatch call already threads through as <c>ChannelEventId</c>, so once this row
/// exists that correlation resolves to something real instead of null. Idempotent: an EventSub re-delivery
/// skips rather than double-logs. Stores the Twitch user id in <c>Data</c> rather than resolving
/// <c>ChannelEvent.UserId</c> — the internal Users FK — since, unlike the per-event-type
/// <c>TwitchAlertHandlerBase</c> subclasses, this generic role-broadcast path has no per-event user-resolution
/// step of its own to reuse.
/// </summary>
internal static class RoleBroadcastChannelEventLogger
{
    public static async Task LogAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        Guid eventId,
        string twitchUserId,
        string type,
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
                Data = JsonSerializer.Serialize(new { userId = twitchUserId }),
            }
        );
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Broadcasts moderator role grants (<c>channel.moderator.add</c>) to the dashboard AND, identically, to overlays.</summary>
public sealed class ModeratorAddedBroadcastHandler : IEventHandler<ModeratorAddedEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IHubUserEnricher _enricher;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;

    public ModeratorAddedBroadcastHandler(
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

    public async Task HandleAsync(ModeratorAddedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        HubUserEnrichment? enrichment = await _enricher.EnrichAsync(
            @event.BroadcasterId,
            @event.UserId,
            ct
        );

        RoleChangedAlertDto dto = new(
            @event.UserId,
            @event.UserDisplayName,
            @event.UserLogin,
            enrichment?.AvatarUrl,
            enrichment?.Pronouns,
            enrichment?.CommunityStanding
        );

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "moderator_added",
            dto,
            ct,
            userId: @event.UserId,
            userDisplayName: @event.UserDisplayName
        );

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            @event.BroadcasterId,
            "moderator_added",
            dto,
            @event.EventId.ToString(),
            ct
        );
    }
}

/// <summary>Broadcasts moderator role revocations (<c>channel.moderator.remove</c>) to the dashboard AND overlays.</summary>
public sealed class ModeratorRemovedBroadcastHandler : IEventHandler<ModeratorRemovedEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IHubUserEnricher _enricher;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;

    public ModeratorRemovedBroadcastHandler(
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

    public async Task HandleAsync(ModeratorRemovedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        HubUserEnrichment? enrichment = await _enricher.EnrichAsync(
            @event.BroadcasterId,
            @event.UserId,
            ct
        );

        RoleChangedAlertDto dto = new(
            @event.UserId,
            @event.UserDisplayName,
            @event.UserLogin,
            enrichment?.AvatarUrl,
            enrichment?.Pronouns,
            enrichment?.CommunityStanding
        );

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "moderator_removed",
            dto,
            ct,
            userId: @event.UserId,
            userDisplayName: @event.UserDisplayName
        );

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            @event.BroadcasterId,
            "moderator_removed",
            dto,
            @event.EventId.ToString(),
            ct
        );
    }
}

/// <summary>Broadcasts VIP role grants (<c>channel.vip.add</c>) to the dashboard AND, identically, to overlays.</summary>
public sealed class VipAddedBroadcastHandler : IEventHandler<VipAddedEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IHubUserEnricher _enricher;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;

    public VipAddedBroadcastHandler(
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

    public async Task HandleAsync(VipAddedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        await RoleBroadcastChannelEventLogger.LogAsync(
            _db,
            @event.BroadcasterId,
            @event.EventId,
            @event.UserId,
            "channel.vip.add",
            ct
        );

        HubUserEnrichment? enrichment = await _enricher.EnrichAsync(
            @event.BroadcasterId,
            @event.UserId,
            ct
        );

        RoleChangedAlertDto dto = new(
            @event.UserId,
            @event.UserDisplayName,
            @event.UserLogin,
            enrichment?.AvatarUrl,
            enrichment?.Pronouns,
            enrichment?.CommunityStanding
        );

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "vip_added",
            dto,
            ct,
            userId: @event.UserId,
            userDisplayName: @event.UserDisplayName
        );

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            @event.BroadcasterId,
            "vip_added",
            dto,
            @event.EventId.ToString(),
            ct
        );
    }
}

/// <summary>Broadcasts VIP role revocations (<c>channel.vip.remove</c>) to the dashboard AND, identically, to overlays.</summary>
public sealed class VipRemovedBroadcastHandler : IEventHandler<VipRemovedEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IHubUserEnricher _enricher;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;

    public VipRemovedBroadcastHandler(
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

    public async Task HandleAsync(VipRemovedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        await RoleBroadcastChannelEventLogger.LogAsync(
            _db,
            @event.BroadcasterId,
            @event.EventId,
            @event.UserId,
            "channel.vip.remove",
            ct
        );

        HubUserEnrichment? enrichment = await _enricher.EnrichAsync(
            @event.BroadcasterId,
            @event.UserId,
            ct
        );

        RoleChangedAlertDto dto = new(
            @event.UserId,
            @event.UserDisplayName,
            @event.UserLogin,
            enrichment?.AvatarUrl,
            enrichment?.Pronouns,
            enrichment?.CommunityStanding
        );

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "vip_removed",
            dto,
            ct,
            userId: @event.UserId,
            userDisplayName: @event.UserDisplayName
        );

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            @event.BroadcasterId,
            "vip_removed",
            dto,
            @event.EventId.ToString(),
            ct
        );
    }
}
