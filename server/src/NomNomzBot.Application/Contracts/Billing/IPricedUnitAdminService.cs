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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Billing;

namespace NomNomzBot.Application.Contracts.Billing;

/// <summary>
/// The admin write surface for the priced-unit catalogue (S-ADMIN-4d) — the table the owner fills in to give
/// a real per-unit price to a usage key that <c>UsageRecord</c>/<c>TtsUsageRecord</c> already measures.
/// Ships EMPTY: a unit with no row here is unpriced, and cost for it is refused rather than reported as
/// zero (see <c>PricedUnit</c>'s doc comment).
/// </summary>
public interface IPricedUnitAdminService
{
    /// <summary>Every priced unit, ordered by <c>UnitKey</c>, for the admin pricing editor.</summary>
    Task<Result<IReadOnlyList<PricedUnitDto>>> ListPricedUnitsAsync(CancellationToken ct = default);

    /// <summary>
    /// Author a unit's price — creates a new row for an unpriced <c>UnitKey</c>, or overwrites the price of
    /// an already-priced one. Always records an <c>IamAuditLog</c> row naming the acting operator.
    /// </summary>
    Task<Result<PricedUnitDto>> AuthorPricedUnitAsync(
        AuthorPricedUnitRequest request,
        Guid? actorUserId,
        CancellationToken ct = default
    );
}
