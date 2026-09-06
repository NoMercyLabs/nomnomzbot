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
