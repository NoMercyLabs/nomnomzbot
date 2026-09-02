// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY; See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Webhooks.Entities;

namespace NomNomzBot.Domain.CustomEvents.Entities;

/// <summary>
/// A streamer-configured external data source (custom-events.md §1, schema G.13). Produces a normalized
/// <c>custom.&lt;name&gt;</c> event on each ingest; the event fires pipeline triggers, seeds template
/// variables, and pushes live values to overlays. Soft-delete, tenant-scoped.
/// </summary>
public class CustomDataSource : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }

    /// <summary>Lowercase slug — the <c>&lt;name&gt;</c> in <c>custom.&lt;name&gt;</c> triggers and template helpers.</summary>
    [MaxLength(50)]
    public string Name { get; set; } = null!;

    [MaxLength(100)]
    public string DisplayName { get; set; } = null!;

    /// <summary><c>push</c> | <c>poll</c> | <c>socket</c> — how data arrives (custom-events.md D2).</summary>
    [MaxLength(20)]
    public string SourceKind { get; set; } = null!;

    /// <summary>Auto-discovered preset key (<c>pulsoid</c>, <c>hyperate</c>), or null for a hand-rolled source.</summary>
    [MaxLength(50)]
    public string? PresetKey { get; set; }

    /// <summary>Poll or socket endpoint URL; null for push sources (they use <see cref="InboundWebhookEndpointId"/>).</summary>
    [MaxLength(500)]
    public string? EndpointUrl { get; set; }

    /// <summary>
    /// AEAD-sealed auth credential (bearer token / OAuth access token) via <c>ITokenProtector</c>.
    /// Self-describing envelope — key id, nonce, ciphertext in one column. Null when unauthenticated.
    /// </summary>
    public string? AuthSecretCipher { get; set; }

    /// <summary>
    /// JSON field-map — <c>{ "&lt;field&gt;": "&lt;jsonpath&gt;" }</c> extracting named fields from the raw payload
    /// (e.g. <c>{ "bpm": "$.data.heartRate" }</c>). Always non-null; defaults to an empty map.
    /// </summary>
    public string FieldMapJson { get; set; } = "{}";

    /// <summary>Poll interval in seconds (poll sources only). Clamped to a tier-scaled floor; null otherwise.</summary>
    public int? PollIntervalSeconds { get; set; }

    /// <summary>Push sources only — the H.10 inbound-webhook endpoint that receives data for this source.</summary>
    public Guid? InboundWebhookEndpointId { get; set; }

    /// <summary>Disabled until explicitly turned on (default-deny per custom-events.md D8).</summary>
    public bool IsEnabled { get; set; }

    public DateTime? LastReceivedAt { get; set; }

    /// <summary>Instant of the last fetch attempt (success OR failure) — poll sources only (S100a).</summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>The last fetch/ingest error message, or null after a successful attempt (S100a).</summary>
    [MaxLength(1000)]
    public string? LastError { get; set; }

    /// <summary>Consecutive failed attempts since the last success; reset to 0 on success (S100a).</summary>
    public int ConsecutiveFailureCount { get; set; }

    /// <summary>
    /// JSON map <c>{ "&lt;field&gt;": "&lt;error&gt;" }</c> of the field-map entries that failed to resolve on the
    /// most recent ingest (malformed JSONPath syntax, or a path that did not match anything in the payload).
    /// Null when the last ingest had no per-field errors. Fields that DID resolve are unaffected — a source with
    /// one broken mapping among several still ingests the working ones (S100).
    /// </summary>
    [MaxLength(1000)]
    public string? LastFieldErrorsJson { get; set; }

    /// <summary>Earliest instant the next poll attempt may run, set by <c>CustomDataPollBackoffPolicy</c> after a
    /// failure and cleared on success (S100a). Null when there is no pending backoff.</summary>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>Set when <see cref="IsEnabled"/> was auto-disabled after crossing the failure threshold (S100a).</summary>
    public DateTime? DisabledAt { get; set; }

    /// <summary>Human-readable reason the source was auto-disabled (S100a).</summary>
    [MaxLength(500)]
    public string? DisabledReason { get; set; }

    public Guid CreatedByUserId { get; set; }

    // ── Navigations ─────────────────────────────────────────────────────────────

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(CreatedByUserId))]
    public virtual User CreatedByUser { get; set; } = null!;

    [ForeignKey(nameof(InboundWebhookEndpointId))]
    public virtual InboundWebhookEndpoint? InboundWebhookEndpoint { get; set; }
}
