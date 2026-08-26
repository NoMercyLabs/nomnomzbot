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

    /// <summary>
    /// Whether <see cref="BodyTemplate"/> is authored as JSON (S-WEBHOOK-JSON-FALLBACK). When true, the template
    /// must parse as JSON (validated at save time) and is rendered through the JSON-safe leaf-substitution path —
    /// a stored template that somehow fails to parse anyway fails delivery honestly instead of silently falling
    /// back to unescaped plain-text rendering. When false, the template was never intended as JSON (a form post,
    /// plain text) and always renders through the plain-text path. Defaults to true — every outbound delivery is
    /// sent with a hardcoded <c>Content-Type: application/json</c> today, so JSON is the honest default.
    /// </summary>
    public bool BodyIsJson { get; set; } = true;

    /// <summary>Author-supplied headers (also templated), as a JSON object string.</summary>
    public string? CustomHeadersJson { get; set; }

    /// <summary>The AEAD-sealed <c>whsec_</c> signing secret (ITokenProtector envelope; nonce + tag inside).</summary>
    public string SigningSecretEnvelope { get; set; } = null!;

    /// <summary>An overlap-valid sealed secret during rotation (multi-sig).</summary>
    public string? SecondarySigningSecretEnvelope { get; set; }
    public Guid EncryptionKeyId { get; set; }

    public bool IsEnabled { get; set; }
    public int ConsecutiveFailureCount { get; set; }
    public DateTime? DisabledAt { get; set; }
    public string? DisabledReason { get; set; }
    public DateTime? LastDeliveryAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
}
