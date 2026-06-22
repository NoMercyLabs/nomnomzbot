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
