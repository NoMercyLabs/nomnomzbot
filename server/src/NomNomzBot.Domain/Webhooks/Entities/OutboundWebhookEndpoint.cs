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

namespace NomNomzBot.Domain.Webhooks.Entities;

/// <summary>
/// A tenant-configured outbound webhook target (webhooks.md §1, schema H.8). Stores the per-endpoint
/// <c>whsec_</c> signing secret (AEAD-wrapped), the subscribed event set, and the author template/headers; it
/// pins to an H.7 <c>HttpEgressAllowlist</c> row for the actual SSRF boundary (reuse, not duplicate).
/// Auto-disables after consecutive failures.
/// </summary>
public class OutboundWebhookEndpoint : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }
    public string Name { get; set; } = null!;

    /// <summary>The endpoint FQDN — must mirror the pinned H.7 allowlist row.</summary>
    public string Fqdn { get; set; } = null!;
    public Guid? HttpEgressAllowlistId { get; set; }
    public string? Path { get; set; }

    /// <summary>Event types this endpoint receives (<c>*</c> = all), as a JSON array string.</summary>
    public string SubscribedEventTypesJson { get; set; } = "[]";
    public string? BodyTemplate { get; set; }

    /// <summary>Author-supplied headers (also templated), as a JSON object string.</summary>
    public string? CustomHeadersJson { get; set; }

    public string SigningSecretCipher { get; set; } = null!;
    public string SigningSecretNonce { get; set; } = null!;

    /// <summary>An overlap-valid secret during rotation (multi-sig).</summary>
    public string? SecondarySigningSecretCipher { get; set; }
    public string? SecondarySigningSecretNonce { get; set; }
    public Guid EncryptionKeyId { get; set; }

    public bool IsEnabled { get; set; }
    public int ConsecutiveFailureCount { get; set; }
    public DateTime? DisabledAt { get; set; }
    public string? DisabledReason { get; set; }
    public DateTime? LastDeliveryAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
}
