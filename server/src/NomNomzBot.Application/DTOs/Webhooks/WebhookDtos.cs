// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Webhooks.Enums;

namespace NomNomzBot.Application.DTOs.Webhooks;

/// <summary>An inbound webhook endpoint view (webhooks.md §4). Never exposes the verification secret — only a set/unset flag.</summary>
public sealed record InboundWebhookEndpointDto(
    Guid Id,
    string Name,
    WebhookAdapterKind Adapter,
    string IngestUrl,
    bool VerificationSecretSet,
    Guid? TargetPipelineId,
    string? TargetEventType,
    bool IsEnabled,
    DateTime? LastReceivedAt,
    long ReceiveCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>A buffered inbound webhook request the dispatcher hands to the adapter (webhooks.md §4). Raw bytes are exact.</summary>
public sealed record InboundWebhookRequest
{
    public required string Token { get; init; }
    public required string Method { get; init; }
    public required string ContentType { get; init; }
    public required IReadOnlyDictionary<string, string> Headers { get; init; }
    public required ReadOnlyMemory<byte> RawBody { get; init; }
    public required DateTime ReceivedAtUtc { get; init; }
    public required string RemoteIpHash { get; init; }
}

/// <summary>The verdict of an adapter's signature/secret check.</summary>
public sealed record WebhookVerification(bool IsValid, WebhookRejectReason? Reason);

/// <summary>The dispatcher's typed outcome (webhooks.md §4) — the controller maps <c>HttpStatus</c> to the response.</summary>
public sealed record InboundDispatchResult(
    bool Verified,
    bool WasDuplicate,
    Guid? JournalEventId,
    long StreamPosition,
    string EventType,
    WebhookRejectReason? RejectReason,
    int HttpStatus,
    Guid? ResolvedEndpointId,
    Guid? ResolvedBroadcasterId,
    WebhookAdapterKind? ResolvedAdapter
);

/// <summary>A normalized inbound event: the kind token, the dedupe id, and a flat variable bag (webhook.*/payload.*).</summary>
public sealed record ParsedInboundEvent(
    string Kind,
    string ProviderEventId,
    IReadOnlyDictionary<string, string> Variables
);

// ── Outbound endpoints (webhooks.md §3.5 / §4) ──

public sealed record OutboundWebhookEndpointDto(
    Guid Id,
    string Name,
    string Fqdn,
    string? Path,
    IReadOnlyList<string> SubscribedEventTypes,
    bool IsEnabled,
    int ConsecutiveFailureCount,
    DateTime? DisabledAt,
    string? DisabledReason,
    DateTime? LastDeliveryAt,
    DateTime? LastSuccessAt,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>Create result — carries the plaintext <c>whsec_</c> secret ONCE (never re-readable).</summary>
public sealed record OutboundWebhookEndpointCreatedDto(
    OutboundWebhookEndpointDto Endpoint,
    string SigningSecret
);

public sealed record CreateOutboundWebhookRequest
{
    public required string Name { get; init; }

    /// <summary>Must match an enabled HttpEgressAllowlist (H.7) row for the tenant.</summary>
    public required string Fqdn { get; init; }
    public string? Path { get; init; }
    public required List<string> SubscribedEventTypes { get; init; }
    public string? BodyTemplate { get; init; }
    public Dictionary<string, string>? CustomHeaders { get; init; }
    public bool IsEnabled { get; init; } = true;
}

public sealed record UpdateOutboundWebhookRequest
{
    public string? Name { get; init; }
    public List<string>? SubscribedEventTypes { get; init; }
    public string? BodyTemplate { get; init; }
    public Dictionary<string, string>? CustomHeaders { get; init; }
    public bool? IsEnabled { get; init; }
}

public sealed record WebhookTestResultDto(
    bool Delivered,
    int? ResponseCode,
    int DurationMs,
    string? Error
);

/// <summary>The outcome of enqueuing one outbound delivery (webhooks.md §4).</summary>
public sealed record OutboundEnqueueResult(
    Guid EndpointId,
    Guid WebhookMessageId,
    long DeliveryId,
    WebhookDeliveryStatus Status
);

/// <summary>
/// One row of an outbound endpoint's delivery log (webhooks.md §5.1) — the visibility that makes a webhook
/// integration debuggable: which event, which attempt, the resulting status/HTTP code, and the next retry time.
/// The rendered request body is intentionally omitted (it can carry sensitive payload); use the receiver's own logs.
/// </summary>
public sealed record OutboundWebhookDeliveryDto(
    long Id,
    Guid EndpointId,
    string EventType,
    int Attempt,
    string Status,
    int? ResponseCode,
    int? DurationMs,
    DateTime? NextRetryAt,
    string? Error,
    DateTime CreatedAt
);

/// <summary>Generic / Standard-Webhooks adapter config (Zapier/IFTTT/Make/Stream Deck/custom).</summary>
public sealed record GenericInboundConfig(
    string? SignatureHeaderName,
    string? SignaturePrefix,
    string? SigningStringTemplate,
    string? TimestampHeaderName,
    string? SharedSecretBodyField,
    string EventKindJsonPath,
    string ProviderEventIdJsonPath
);

public sealed record CreateInboundWebhookRequest
{
    public required string Name { get; init; }
    public required WebhookAdapterKind Adapter { get; init; }

    /// <summary>The provider token / shared secret. AEAD-encrypted on store, never persisted in plaintext.</summary>
    public required string VerificationSecret { get; init; }
    public Guid? TargetPipelineId { get; init; }
    public string? TargetEventType { get; init; }
    public GenericInboundConfig? GenericConfig { get; init; }
    public bool IsEnabled { get; init; } = true;
}

public sealed record UpdateInboundWebhookRequest
{
    public string? Name { get; init; }

    /// <summary>When present, rotates (re-encrypts) the verification secret.</summary>
    public string? VerificationSecret { get; init; }
    public Guid? TargetPipelineId { get; init; }
    public string? TargetEventType { get; init; }
    public GenericInboundConfig? GenericConfig { get; init; }
    public bool? IsEnabled { get; init; }
}
