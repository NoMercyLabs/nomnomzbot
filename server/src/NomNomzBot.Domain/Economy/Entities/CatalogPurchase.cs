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

using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Economy.Entities;

/// <summary>
/// An immutable record of one catalog redemption (economy.md K.11). Snapshots the item name + cost paid and
/// links the debiting <c>LedgerEntryId</c>. APPEND-ONLY: no <c>UpdatedAt</c>/<c>DeletedAt</c>; tenant-scoped,
/// keyed by a <c>long</c> identity. A refund is recorded by flipping <c>Status</c> via a new reversing ledger
/// entry (the purchase row itself is the audit anchor).
/// </summary>
public class CatalogPurchase : ITenantScoped
{
    public long Id { get; set; }
    public Guid BroadcasterId { get; set; }
    public Guid CatalogItemId { get; set; }
    public Guid BuyerAccountId { get; set; }
    public Guid BuyerUserId { get; set; }
    public long CostPaid { get; set; }
    public string ItemNameSnapshot { get; set; } = null!;
    public CatalogPurchaseStatus Status { get; set; }
    public long? LedgerEntryId { get; set; }
    public string? InputArgs { get; set; }

    /// <summary>A client-supplied token making a retried purchase a no-op (returns the original, no double-charge).</summary>
    public string? IdempotencyKey { get; set; }
    public DateTime CreatedAt { get; set; }
}
