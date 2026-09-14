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
using NomNomzBot.Domain.Commands.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Widgets.EventHandlers;

/// <summary>The wire shape the Alert widget's voice_trigger card reads: word + live count + sticker image.</summary>
public sealed record VoiceTriggerWidgetEventPayload(
    string Word,
    int Count,
    string? StickerImageUrl
);

/// <summary>
/// Routes <see cref="VoiceTriggerFiredEvent"/> to every enabled widget subscribed to the <c>voice_trigger</c>
/// event type — same routing shape as <c>GoalWidgetEventHandler</c> (widget-quality-audit §1 pattern): read
/// enabled widgets for the channel server-side, filter by <c>EventSubscriptions</c> client-side (a JSON-converted
/// column <c>List&lt;string&gt;.Contains</c> cannot translate to SQL), push one <c>WidgetEvent</c> per subscriber
/// over the existing <c>OverlayHub</c>. The Alert widget (<c>alerts.vue</c>) is the first-party subscriber: it
/// renders <see cref="VoiceTriggerWidgetEventPayload.StickerImageUrl"/> as a transient image card, same
/// enter/hold/exit timing as every other alert.
/// </summary>
public sealed class VoiceTriggerWidgetEventHandler : IEventHandler<VoiceTriggerFiredEvent>
{
    private const string EventType = "voice_trigger";

    private readonly IApplicationDbContext _db;
    private readonly IWidgetEventNotifier _overlay;

    public VoiceTriggerWidgetEventHandler(IApplicationDbContext db, IWidgetEventNotifier overlay)
    {
        _db = db;
        _overlay = overlay;
    }

    public async Task HandleAsync(
        VoiceTriggerFiredEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        VoiceTriggerWidgetEventPayload payload = new(
            @event.Word,
            @event.NewCount,
            @event.StickerImageUrl
        );

        List<Widget> candidates = await _db
            .Widgets.AsNoTracking()
            .Where(w => w.BroadcasterId == @event.BroadcasterId && w.IsEnabled)
            .ToListAsync(cancellationToken);
        IEnumerable<Widget> subscribers = candidates.Where(w =>
            w.EventSubscriptions.Contains(EventType)
        );

        foreach (Widget widget in subscribers)
        {
            await _overlay.SendWidgetEventAsync(
                @event.BroadcasterId,
                widget.Id,
                EventType,
                payload,
                cancellationToken
            );
        }
    }
}
