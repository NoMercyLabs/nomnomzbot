// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Widgets.Entities;

/// <summary>
/// The exact widget-event push made by <c>WidgetAlertDispatch.RouteAsync</c> — <c>EventType</c> + the raw JSON
/// <c>data</c> object, verbatim, no transformation. This is the foundation for a later "Replay" action on the
/// dashboard activity feed: because it is captured at the single choke point every transient alert AND
/// <c>tts_speak</c> already routes through, a replay can re-broadcast it byte-for-byte with zero re-derivation —
/// it must NEVER re-run currency grants, loyalty points, reward fulfillment, or any other persistent side effect,
/// since those already ran once when the origin event fired.
/// </summary>
/// <remarks>
/// Not soft-deletable: this is an append-only capture log pruned by row count (bounded to the same recency
/// window as the dashboard activity feed), not a record subject to the global soft-delete filter. It IS
/// <see cref="ITenantScoped"/> — a non-nullable <see cref="BroadcasterId"/> — so the tenant query filter
/// isolates it, and it dies with the channel (channel-delete blast radius: overlays category, alongside
/// <see cref="Widget"/>).
/// </remarks>
public class RenderedAlertCapture : BaseEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BroadcasterId { get; set; }

    // Matches ChannelEvent.Type's 100-char allowance (long Twitch EventSub type strings).
    [MaxLength(100)]
    public string EventType { get; set; } = null!;

    // The verbatim `data` object RouteAsync pushed to the widget, serialized as JSON.
    public string Payload { get; set; } = null!;

    // FK→ChannelEvents.Id (string, MaxLength 50 — see ChannelEvent.Id) — the activity-feed row that produced
    // this alert, so a later "Replay" action can look a capture up by the feed item the operator clicked. Set
    // from the originating domain event's EventId (IDomainEvent.EventId), which is the SAME id
    // TwitchAlertHandlerBase/TwitchChannelEventLogProjection key the ChannelEvent row by — so this column
    // resolves to a real row whenever one was (or will be) written for that event. Genuinely null for pushes
    // with no corresponding ChannelEvent at all (e.g. tts_speak from TtsUtteranceDispatchedEvent, or the
    // now_playing/track_saved music-state pushes) — never fabricated. Not a database FK: ChannelEvents is
    // written asynchronously by a projection that can race this write, and some values here never resolve
    // (VIP/shoutout events currently log no ChannelEvent row at all).
    [MaxLength(50)]
    public string? ChannelEventId { get; set; }
}
