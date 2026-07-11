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
/// One normalized monetization event (supporter-events.md P.16): a tip, membership, merch order, or charity
/// donation, from any provider, reduced to a single shape carrying a <see cref="Kind"/>. It is a truthful
/// record of what a provider reported — the dedup key <see cref="ProviderTransactionId"/> guarantees a
/// redelivered webhook inserts once. Amounts are stored in minor units (cents) to avoid float drift.
/// </summary>
public class SupporterEvent : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }

    public Guid BroadcasterId { get; set; }

    /// <summary>The provider that reported this event — <c>kofi</c> / <c>patreon</c> / … .</summary>
    [MaxLength(30)]
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>The normalized kind — <c>tip</c> / <c>membership</c> / <c>merch</c> / <c>charity</c>.</summary>
    [MaxLength(20)]
    public string Kind { get; set; } = string.Empty;

    /// <summary>The supporter's name as the provider reported it (may not map to a known viewer).</summary>
    [MaxLength(100)]
    public string SupporterDisplayName { get; set; } = string.Empty;

    /// <summary>The resolved internal user id when the supporter matches a known viewer; null otherwise.</summary>
    public Guid? SupporterUserId { get; set; }

    /// <summary>Amount in minor units (cents); null for kinds without a monetary value.</summary>
    public long? AmountMinor { get; set; }

    [MaxLength(3)]
    public string? Currency { get; set; }

    /// <summary>Membership tier name (memberships only).</summary>
    [MaxLength(50)]
    public string? Tier { get; set; }

    /// <summary>Months (membership) or item count (merch).</summary>
    public int? Quantity { get; set; }

    /// <summary>Merch line-items as normalized JSON (merch only).</summary>
    public string? ItemsJson { get; set; }

    public string? MessageText { get; set; }

    public bool IsRecurring { get; set; }

    /// <summary>The provider's transaction id (or a composite hash where none exists) — the dedup key.</summary>
    [MaxLength(120)]
    public string ProviderTransactionId { get; set; } = string.Empty;

    /// <summary>The normalized raw payload as JSON, retained for audit / re-projection.</summary>
    public string PayloadJson { get; set; } = "{}";

    public DateTime ReceivedAt { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(SupporterUserId))]
    public virtual User? SupporterUser { get; set; }
}
