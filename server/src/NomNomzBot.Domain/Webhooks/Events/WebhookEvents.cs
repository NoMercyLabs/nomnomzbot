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
    /// <summary>The id of the inbound webhook endpoint that received the call.</summary>
    public required Guid InboundEndpointId { get; init; }

    /// <summary>The kind of service that sent the call, as text.</summary>
    public required WebhookAdapterKind Adapter { get; init; }

    /// <summary>The event type that the call became, written as webhook.provider.kind.</summary>
    public required string EventType { get; init; } // "webhook.<provider>.<kind>"

    /// <summary>The id of the stored copy of the event in the journal.</summary>
    public required Guid JournalEventId { get; init; }

    /// <summary>The position of the event in the channel's event stream.</summary>
    public required long StreamPosition { get; init; }

    /// <summary>The id that the sending service gave the event.</summary>
    public required string ProviderEventId { get; init; }

    /// <summary>True when the sending service already sent this event before.</summary>
    public required bool WasDuplicate { get; init; }
}

/// <summary>An inbound webhook was rejected before any side effect, on a RESOLVED endpoint (never the unknown-token 404 path).</summary>
public sealed class InboundWebhookRejectedEvent : DomainEventBase
{
    /// <summary>The id of the inbound webhook endpoint that got the call.</summary>
    public required Guid InboundEndpointId { get; init; }

    /// <summary>The kind of service that sent the call, as text.</summary>
    public required WebhookAdapterKind Adapter { get; init; }

    /// <summary>Why the call was refused, as text.</summary>
    public required WebhookRejectReason Reason { get; init; }

    /// <summary>The HTTP status code sent back to the caller.</summary>
    public required int HttpStatus { get; init; }
}

/// <summary>An outbound delivery was enqueued (a send_webhook action or a matching event fired).</summary>
public sealed class OutboundWebhookEnqueuedEvent : DomainEventBase
{
    /// <summary>The id of the outbound webhook endpoint that gets the message.</summary>
    public required Guid OutboundEndpointId { get; init; }

    /// <summary>The id of the queued message.</summary>
    public required Guid WebhookMessageId { get; init; }

    /// <summary>The type of the event that the message carries.</summary>
    public required string EventType { get; init; }

    /// <summary>The id of the journal event that caused the message. Empty when no journal event caused it.</summary>
    public Guid? JournalEventId { get; init; }
}

/// <summary>An outbound delivery attempt finished (one event per attempt).</summary>
public sealed class OutboundWebhookAttemptedEvent : DomainEventBase
{
    /// <summary>The id of the outbound webhook endpoint.</summary>
    public required Guid OutboundEndpointId { get; init; }

    /// <summary>The id of the message that was sent.</summary>
    public required Guid WebhookMessageId { get; init; }

    /// <summary>The number of this try. The first try is 1.</summary>
    public required int Attempt { get; init; }

    /// <summary>The result of this try, as text.</summary>
    public required WebhookDeliveryStatus Status { get; init; }

    /// <summary>The HTTP status code the receiver sent back. Empty when no response came.</summary>
    public int? ResponseCode { get; init; }

    /// <summary>When the next try will happen, in UTC. Empty when no retry is planned.</summary>
    public DateTime? NextRetryAt { get; init; }
}

/// <summary>An outbound endpoint was auto-disabled after N consecutive failures.</summary>
public sealed class OutboundWebhookAutoDisabledEvent : DomainEventBase
{
    /// <summary>The id of the outbound webhook endpoint that was turned off.</summary>
    public required Guid OutboundEndpointId { get; init; }

    /// <summary>How many sends in a row failed.</summary>
    public required int ConsecutiveFailureCount { get; init; }

    /// <summary>A short text that explains why the endpoint was turned off.</summary>
    public required string Reason { get; init; }
}
