// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Supporters.Entities;

/// <summary>
/// A monetization provider a broadcaster has connected (supporter-events.md P.15). One row per
/// (broadcaster, source): which provider, how it ingests, whether it is live. Webhook providers (Ko-fi,
/// Patreon, …) verify + ingest through the shared inbound-webhook plane; OAuth providers resolve tokens
/// from the integration vault; socket/poll providers hold an AEAD API key in <see cref="AuthSecretCipher"/>.
/// Default-deny: a connection ingests nothing until <see cref="IsEnabled"/> is set.
/// </summary>
public class SupporterConnection : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }

    public Guid BroadcasterId { get; set; }

    /// <summary>The provider key — <c>kofi</c> / <c>patreon</c> / <c>streamelements</c> / … (supporter-events.md §0 D2).</summary>
    [MaxLength(30)]
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>How this provider delivers events — <c>webhook</c> / <c>socket</c> / <c>ws</c> / <c>poll</c>.</summary>
    [MaxLength(20)]
    public string ConnectionMode { get; set; } = string.Empty;

    /// <summary>AEAD-sealed API key / webhook secret for socket/poll providers; null when the secret lives on the
    /// linked webhook endpoint or the OAuth vault.</summary>
    public string? AuthSecretCipher { get; set; }

    /// <summary>Set for OAuth providers (Patreon/Shopify/TreatStream) — the vaulted integration connection.</summary>
    public Guid? IntegrationConnectionId { get; set; }

    /// <summary>Set for webhook providers — the inbound endpoint whose verified deliveries feed this connection.</summary>
    public Guid? InboundWebhookEndpointId { get; set; }

    public bool IsEnabled { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "idle";

    public DateTime? LastEventAt { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}
