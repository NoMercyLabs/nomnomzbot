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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Supporters.Events;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Widgets.EventHandlers;

/// <summary>
/// The real wire shape a <c>supporter.tip</c>/<c>supporter.membership</c>/<c>supporter.merch</c>/
/// <c>supporter.charity</c> widget event carries — every field <see cref="SupporterEventReceived"/>
/// actually publishes, reduced to what an overlay needs (no internal ids). Amounts stay in minor units
/// (cents) so the widget, not this payload, decides how to format them.
/// </summary>
public sealed record SupporterAlertPayload(
    string Kind,
    string SupporterDisplayName,
    long? AmountMinor,
    string? Currency,
    string? Tier,
    int? Quantity,
    string? MessageText,
    bool IsRecurring
)
{
    /// <summary>
    /// Canonical subject name, matching the widget-facing vocabulary every alert payload exposes
    /// (<c>AlertDtos.cs</c>) — so a widget reads <c>data.user</c> for a supporter event exactly as it does
    /// for a raid or a follow, instead of having to know this payload spells it <c>supporterDisplayName</c>.
    /// </summary>
    public string User => SupporterDisplayName;

    /// <summary>Canonical headline scalar — the supported amount in major units, null when the kind carries none.</summary>
    public decimal? Amount => AmountMinor is null ? null : AmountMinor.Value / 100m;
}

/// <summary>
/// Routes <see cref="SupporterEventReceived"/> (supporter-events.md P.16 — a normalized tip/membership/merch/
/// charity event from any provider) to every enabled widget subscribed to its <c>supporter.&lt;kind&gt;</c>
/// event type (widget-quality-audit §1: <c>alerts.vue</c>/<c>event_ticker.vue</c> already declare these four
/// event types in their settings schema, but nothing ever routed a real event to them — they rendered the
/// event's true fields under a guessed shape, or never rendered anything at all). Mirrors
/// <see cref="GoalWidgetEventHandler"/>'s routing predicate (enabled + <see cref="Widget.EventSubscriptions"/>
/// contains the event type), reproduced here for the same reason: the API hub-broadcaster layer's
/// <c>WidgetAlertRouting</c> is out of reach from Infrastructure.
/// </summary>
public sealed class SupporterWidgetEventHandler : IEventHandler<SupporterEventReceived>
{
    private readonly IApplicationDbContext _db;
    private readonly IWidgetEventNotifier _overlay;

    public SupporterWidgetEventHandler(IApplicationDbContext db, IWidgetEventNotifier overlay)
    {
        _db = db;
        _overlay = overlay;
    }

    public async Task HandleAsync(
        SupporterEventReceived @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        string eventType = $"supporter.{@event.Kind}";
        SupporterAlertPayload payload = new(
            @event.Kind,
            @event.SupporterDisplayName,
            @event.AmountMinor,
            @event.Currency,
            @event.Tier,
            @event.Quantity,
            @event.MessageText,
            @event.IsRecurring
        );

        // EventSubscriptions is a JSON-converted column — List<string>.Contains cannot translate to SQL, so the
        // channel/enabled filter runs server-side and the subscription check runs client-side (matches
        // GoalWidgetEventHandler's read for the identical reason).
        List<Widget> candidates = await _db
            .Widgets.AsNoTracking()
            .Where(w => w.BroadcasterId == @event.BroadcasterId && w.IsEnabled)
            .ToListAsync(cancellationToken);

        foreach (Widget widget in candidates.Where(w => w.EventSubscriptions.Contains(eventType)))
        {
            await _overlay.SendWidgetEventAsync(
                @event.BroadcasterId,
                widget.Id,
                eventType,
                payload,
                cancellationToken
            );
        }
    }
}
