// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
    public DateTime CreatedAt { get; set; }
}
