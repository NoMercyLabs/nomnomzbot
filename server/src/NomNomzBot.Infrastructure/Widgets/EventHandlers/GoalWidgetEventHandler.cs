// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Widgets.EventHandlers;

/// <summary>
/// The wire shape `goal_bar.vue`/`labels.vue`'s <c>nnz.on('goal', ...)</c> handlers expect (widget-quality-audit
/// §1): the authoritative absolute value for one metric, not a delta.
/// </summary>
public sealed record GoalWidgetEventPayload(string Metric, int Value, int Target);

/// <summary>
/// Routes creator-goal domain events (<c>GoalBeganEvent</c>/<c>GoalProgressEvent</c>/<c>GoalEndedEvent</c>) to
/// every enabled widget subscribed to the <c>goal</c> event type — closing the gap the widget-quality audit (§1)
/// found: nothing routed these to <see cref="IWidgetEventNotifier"/>/<c>OverlayHub</c>, so `goal_bar.vue`'s
/// "authoritative goal value" code path, and `labels.vue`'s `follower_count`/`sub_count` modes, were dead against
/// live traffic. Mirrors the routing predicate <c>WidgetAlertRouting.Subscribers</c> uses for decorated alerts
/// (enabled + <see cref="Widget.EventSubscriptions"/> contains the event type) — that helper lives in the API
/// hub-broadcaster layer (out of reach from Infrastructure), so the tiny predicate is reproduced here rather than
/// referenced.
/// </summary>
public sealed class GoalWidgetEventHandler
    : IEventHandler<GoalBeganEvent>,
        IEventHandler<GoalProgressEvent>,
        IEventHandler<GoalEndedEvent>
{
    private const string EventType = "goal";

    private readonly IApplicationDbContext _db;
    private readonly IWidgetEventNotifier _overlay;

    public GoalWidgetEventHandler(IApplicationDbContext db, IWidgetEventNotifier overlay)
    {
        _db = db;
        _overlay = overlay;
    }

    public Task HandleAsync(GoalBeganEvent @event, CancellationToken cancellationToken = default) =>
        RouteAsync(
            @event.BroadcasterId,
            @event.Type,
            @event.CurrentAmount,
            @event.TargetAmount,
            cancellationToken
        );

    public Task HandleAsync(
        GoalProgressEvent @event,
        CancellationToken cancellationToken = default
    ) =>
        RouteAsync(
            @event.BroadcasterId,
            @event.Type,
            @event.CurrentAmount,
            @event.TargetAmount,
            cancellationToken
        );

    public Task HandleAsync(GoalEndedEvent @event, CancellationToken cancellationToken = default) =>
        RouteAsync(
            @event.BroadcasterId,
            @event.Type,
            @event.CurrentAmount,
            @event.TargetAmount,
            cancellationToken
        );

    private async Task RouteAsync(
        Guid broadcasterId,
        string twitchGoalType,
        int currentAmount,
        int targetAmount,
        CancellationToken cancellationToken
    )
    {
        if (broadcasterId == Guid.Empty)
            return;

        string? metric = MapMetric(twitchGoalType);
        if (metric is null)
            return;

        GoalWidgetEventPayload payload = new(metric, currentAmount, targetAmount);

        // EventSubscriptions is a JSON-converted column (JsonValueConverter) — List<string>.Contains cannot
        // translate to SQL, so the channel/enabled filter runs server-side and the subscription check runs
        // client-side over that already-narrow result set.
        List<Widget> candidates = await _db
            .Widgets.AsNoTracking()
            .Where(w => w.BroadcasterId == broadcasterId && w.IsEnabled)
            .ToListAsync(cancellationToken);
        IEnumerable<Widget> subscribers = candidates.Where(w =>
            w.EventSubscriptions.Contains(EventType)
        );

        foreach (Widget widget in subscribers)
        {
            await _overlay.SendWidgetEventAsync(
                broadcasterId,
                widget.Id,
                EventType,
                payload,
                cancellationToken
            );
        }
    }

    /// <summary>
    /// Twitch's creator-goal <c>type</c> field (<c>channel.goal.*</c>) to the widget library's `metric` vocabulary
    /// (`goal_bar.vue`/`labels.vue`: <c>followers</c>/<c>subs</c>). Twitch has no bits-denominated goal type, so
    /// the widgets' speculative `bits` metric option never has a live event source — an unmapped/unknown Twitch
    /// goal type intentionally routes to nothing rather than guessing.
    /// </summary>
    private static string? MapMetric(string twitchGoalType) =>
        twitchGoalType switch
        {
            "follower" or "followers" => "followers",
            "subscription"
            or "subscription_count"
            or "new_subscription"
            or "new_subscription_count" => "subs",
            _ => null,
        };
}
