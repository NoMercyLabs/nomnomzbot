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
/// A tenant-configured inbound webhook endpoint (webhooks.md §1, schema H.10). The opaque <see cref="Token"/> is
/// the unguessable URL path segment; the per-provider verification secret is AEAD-wrapped. A verified hit runs
/// <see cref="TargetPipelineId"/> or fans out via the event-response service.
/// </summary>
public class InboundWebhookEndpoint : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }
    public string Name { get; set; } = null!;

    /// <summary>The opaque unguessable per-endpoint token (the URL path segment).</summary>
    public string Token { get; set; } = null!;
    public WebhookAdapterKind AdapterKind { get; set; }

    /// <summary>The AEAD-sealed per-provider verification secret (ITokenProtector envelope; nonce + tag inside).</summary>
    public string VerificationSecretEnvelope { get; set; } = null!;
    public Guid EncryptionKeyId { get; set; }

    /// <summary>Generic-adapter config (signature header / signing-string / shared-secret field) as JSON.</summary>
    public string? GenericConfigJson { get; set; }

    public Guid? TargetPipelineId { get; set; }
    public string? TargetEventType { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime? LastReceivedAt { get; set; }
    public long ReceiveCount { get; set; }
}
