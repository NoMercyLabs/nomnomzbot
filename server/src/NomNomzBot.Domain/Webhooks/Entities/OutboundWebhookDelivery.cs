// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Webhooks.Enums;

namespace NomNomzBot.Domain.Webhooks.Entities;

/// <summary>
/// One outbound delivery attempt (webhooks.md §1, schema H.9). APPEND-ONLY — one row per attempt, scanned by the
/// retry worker on <c>(Status, NextRetryAt)</c>. <see cref="WebhookMessageId"/> is the <c>webhook-id</c> the
/// receiver dedupes on.
/// </summary>
public class OutboundWebhookDelivery : ITenantScoped
{
    public long Id { get; set; }
    public Guid BroadcasterId { get; set; }
    public Guid EndpointId { get; set; }
    public Guid WebhookMessageId { get; set; }
    public Guid? JournalEventId { get; set; }
    public string EventType { get; set; } = null!;

    /// <summary>The rendered request body, stored so a retry re-sends the exact same bytes (re-signed each attempt).</summary>
    public string RenderedBody { get; set; } = null!;
    public int Attempt { get; set; }
    public WebhookDeliveryStatus Status { get; set; }
    public int? ResponseCode { get; set; }
    public int? DurationMs { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
}
