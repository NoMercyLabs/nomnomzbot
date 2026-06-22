// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
    public int Attempt { get; set; }
    public WebhookDeliveryStatus Status { get; set; }
    public int? ResponseCode { get; set; }
    public int? DurationMs { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
}
