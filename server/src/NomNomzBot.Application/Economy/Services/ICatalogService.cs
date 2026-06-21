// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;

namespace NomNomzBot.Application.Economy.Services;

/// <summary>
/// Store catalog + redemptions (economy.md §3.4) — the spend surface. Items are CRUD; a purchase debits the
/// buyer through the ledger, records an immutable <c>CatalogPurchase</c>, fires the item's effect, and a refund
/// posts a reversing credit.
/// </summary>
public interface ICatalogService
{
    Task<Result<PagedList<CatalogItemDto>>> ListItemsAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    Task<Result<CatalogItemDto>> GetItemAsync(
        Guid broadcasterId,
        Guid itemId,
        CancellationToken ct = default
    );

    /// <summary>Creates an item (normalizes the name, rejects a duplicate with <c>ALREADY_EXISTS</c>, validates cost).</summary>
    Task<Result<CatalogItemDto>> CreateItemAsync(
        Guid broadcasterId,
        CreateCatalogItemRequest request,
        CancellationToken ct = default
    );

    /// <summary>Partial (PATCH) update — null fields are unchanged; re-normalizes the name on change.</summary>
    Task<Result<CatalogItemDto>> UpdateItemAsync(
        Guid broadcasterId,
        Guid itemId,
        UpdateCatalogItemRequest request,
        CancellationToken ct = default
    );

    /// <summary>Soft-deletes an item (existing purchases keep their name snapshot).</summary>
    Task<Result> DeleteItemAsync(Guid broadcasterId, Guid itemId, CancellationToken ct = default);

    /// <summary>
    /// The redemption flow: enforces enabled/permission/cooldown/stock, debits <c>Cost</c> via the ledger
    /// (INSUFFICIENT_FUNDS bubbles), records a completed <c>CatalogPurchase</c>, and publishes
    /// <c>CatalogItemPurchasedEvent</c> (which carries the <c>PipelineId</c> that triggers the item's effect).
    /// </summary>
    Task<Result<CatalogPurchaseDto>> PurchaseAsync(
        Guid broadcasterId,
        PurchaseRequest request,
        CancellationToken ct = default
    );

    /// <summary>Reverses a completed purchase: a refunding credit + restored stock + an append-only refunded row.</summary>
    Task<Result<CatalogPurchaseDto>> RefundPurchaseAsync(
        Guid broadcasterId,
        long purchaseId,
        RefundRequest request,
        CancellationToken ct = default
    );

    Task<Result<PagedList<CatalogPurchaseDto>>> ListPurchasesAsync(
        Guid broadcasterId,
        PurchaseFilter filter,
        PaginationParams pagination,
        CancellationToken ct = default
    );
}
