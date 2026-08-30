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

    // The verbatim `data` object RouteAsync pushed to the widget, serialized as JSON. No ChannelEvent
    // correlation yet — RouteAsync's callers do not thread a ChannelEvent.Id through today (some pushes,
    // e.g. tts_speak from TtsUtteranceDispatchedEvent, have no ChannelEvent row at all); a later slice can
    // add that correlation once the call sites are revisited.
    public string Payload { get; set; } = null!;
}
