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

namespace NomNomzBot.Domain.Alerts.Entities;

/// <summary>
/// One entry in the channel's single on-air alert queue (widgets-overlays.md §1.2): "the ONE alert queue across
/// every platform connection" — a Twitch sub, a Kick sub and a Patreon pledge are alerts in the same queue, never
/// three separate widget instances. A row is written the moment a qualifying event is received, BEFORE anything
/// asks whether an OBS browser source is even attached to the alert system surface — so a supporter event still
/// produces a queued, dispatchable alert with zero widgets installed and zero overlays connected.
/// <para>
/// <see cref="Status"/> is the delivery-honesty ledger this project has paid twice to learn it needs (TTS once
/// reported "spoke" with nothing attached; an overlay test button once reported success against no listener):
/// a row starts <see cref="AlertQueueStatus.Queued"/> and only ever moves to
/// <see cref="AlertQueueStatus.Delivered"/> after a live overlay connection actually received the push
/// (<c>IOverlayPresenceRegistry.IsWidgetAttached</c>) — never on the strength of the push having been attempted.
/// </para>
/// </summary>
/// <remarks>
/// Not soft-deletable: an append-only, bounded-by-count log (pruned on write, mirroring
/// <c>RenderedAlertCapture</c>), not a record subject to the global soft-delete filter. It IS
/// <see cref="ITenantScoped"/> — a non-nullable <see cref="BroadcasterId"/> — so the tenant query filter isolates
/// it, and it dies with the channel (channel-delete blast radius: overlays category, alongside
/// <c>RenderedAlertCapture</c> and <c>Widget</c>).
/// </remarks>
public class AlertQueueEntry : BaseEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BroadcasterId { get; set; }

    /// <summary>
    /// The platform/integration that produced the alert — <c>twitch</c>, <c>kick</c>, <c>youtube</c>, <c>x</c>,
    /// or a supporter <c>SourceKey</c> (<c>patreon</c>, <c>shopify</c>, <c>treatstream</c>, <c>kofi</c>, …).
    /// This IS the cross-platform attribution the dashboard queue view reads — never inferred from
    /// <see cref="Kind"/>.
    /// </summary>
    [MaxLength(50)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>The alert event type — matches the widget-facing vocabulary (<c>follow</c>, <c>supporter.tip</c>, …).</summary>
    [MaxLength(100)]
    public string Kind { get; set; } = string.Empty;

    /// <summary>The decorated alert payload, verbatim, serialized as JSON — the same shape pushed to the overlay.</summary>
    public string PayloadJson { get; set; } = "{}";

    [MaxLength(20)]
    public string Status { get; set; } = AlertQueueStatus.Queued;

    /// <summary>Set the instant a live overlay connection actually received this alert; null while still queued.</summary>
    public DateTime? DeliveredAt { get; set; }
}

/// <summary>The <see cref="AlertQueueEntry.Status"/> state machine — two terminal-free states, honesty over ceremony.</summary>
public static class AlertQueueStatus
{
    /// <summary>Written to the queue; no live overlay connection has received it yet.</summary>
    public const string Queued = "queued";

    /// <summary>A live overlay connection actually received the push at <see cref="AlertQueueEntry.DeliveredAt"/>.</summary>
    public const string Delivered = "delivered";
}
