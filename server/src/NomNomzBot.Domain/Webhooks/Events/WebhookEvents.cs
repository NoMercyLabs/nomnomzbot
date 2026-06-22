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

namespace NomNomzBot.Domain.Webhooks.Events;

/// <summary>An inbound webhook was verified, deduped, and journaled (Source="webhook"); fans out to pipelines/event-responses.</summary>
public sealed class InboundWebhookReceivedEvent : DomainEventBase
{
    public required Guid InboundEndpointId { get; init; }
    public required WebhookAdapterKind Adapter { get; init; }
    public required string EventType { get; init; } // "webhook.<provider>.<kind>"
    public required Guid JournalEventId { get; init; }
    public required long StreamPosition { get; init; }
    public required string ProviderEventId { get; init; }
    public required bool WasDuplicate { get; init; }
}

/// <summary>An inbound webhook was rejected before any side effect, on a RESOLVED endpoint (never the unknown-token 404 path).</summary>
public sealed class InboundWebhookRejectedEvent : DomainEventBase
{
    public required Guid InboundEndpointId { get; init; }
    public required WebhookAdapterKind Adapter { get; init; }
    public required WebhookRejectReason Reason { get; init; }
    public required int HttpStatus { get; init; }
}

/// <summary>An outbound delivery was enqueued (a send_webhook action or a matching event fired).</summary>
public sealed class OutboundWebhookEnqueuedEvent : DomainEventBase
{
    public required Guid OutboundEndpointId { get; init; }
    public required Guid WebhookMessageId { get; init; }
    public required string EventType { get; init; }
    public Guid? JournalEventId { get; init; }
}

/// <summary>An outbound delivery attempt finished (one event per attempt).</summary>
public sealed class OutboundWebhookAttemptedEvent : DomainEventBase
{
    public required Guid OutboundEndpointId { get; init; }
    public required Guid WebhookMessageId { get; init; }
    public required int Attempt { get; init; }
    public required WebhookDeliveryStatus Status { get; init; }
    public int? ResponseCode { get; init; }
    public DateTime? NextRetryAt { get; init; }
}

/// <summary>An outbound endpoint was auto-disabled after N consecutive failures.</summary>
public sealed class OutboundWebhookAutoDisabledEvent : DomainEventBase
{
    public required Guid OutboundEndpointId { get; init; }
    public required int ConsecutiveFailureCount { get; init; }
    public required string Reason { get; init; }
}
