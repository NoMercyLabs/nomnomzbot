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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts new subscription alerts to dashboard/overlay clients.</summary>
public sealed class NewSubscriptionBroadcastHandler : IEventHandler<NewSubscriptionEvent>
{
    private readonly IDashboardNotifier _notifier;

    public NewSubscriptionBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(NewSubscriptionEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "subscription",
            new SubscriptionAlertDto(@event.UserId, @event.UserDisplayName, @event.Tier),
            ct,
            userId: @event.UserId,
            userDisplayName: @event.UserDisplayName
        );
    }
}

/// <summary>Broadcasts resubscription alerts to dashboard/overlay clients.</summary>
public sealed class ResubscriptionBroadcastHandler : IEventHandler<ResubscriptionEvent>
{
    private readonly IDashboardNotifier _notifier;

    public ResubscriptionBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(ResubscriptionEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "resub",
            new ResubAlertDto(
                @event.UserId,
                @event.UserDisplayName,
                @event.Tier,
                @event.CumulativeMonths,
                @event.StreakMonths,
                @event.Message
            ),
            ct,
            userId: @event.UserId,
            userDisplayName: @event.UserDisplayName
        );
    }
}

/// <summary>Broadcasts gift subscription alerts to dashboard/overlay clients.</summary>
public sealed class GiftSubscriptionBroadcastHandler : IEventHandler<GiftSubscriptionEvent>
{
    private readonly IDashboardNotifier _notifier;

    public GiftSubscriptionBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(GiftSubscriptionEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "gift_sub",
            new GiftSubAlertDto(
                @event.IsAnonymous ? null : @event.GifterUserId,
                @event.IsAnonymous ? "Anonymous" : @event.GifterDisplayName,
                @event.Tier,
                @event.GiftCount,
                @event.IsAnonymous
            ),
            ct,
            userId: @event.IsAnonymous ? null : @event.GifterUserId,
            userDisplayName: @event.IsAnonymous ? "Anonymous" : @event.GifterDisplayName
        );
    }
}
