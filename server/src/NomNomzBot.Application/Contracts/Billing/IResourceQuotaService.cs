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
/// The one write-path seam every <c>LimitedResourceRegistry</c> entry goes through (S-BUDGETS-a). Callers pass
/// the REAL resulting count (a true row count, or a true requested-list-length) — never an estimate — and get
/// back whether it fits. NEAR_FREE resources are checked against the registry's uniform safety baseline
/// (self-host included, never tier-scaled); COST_DRIVING resources delegate to
/// <see cref="IBillingTierService"/> (self-host resolves to unlimited there).
/// </summary>
public interface IResourceQuotaService
{
    /// <summary>
    /// Checks whether <paramref name="resultingCount"/> — the count/length AFTER the create/update under
    /// evaluation — still fits the resource's limit. Does not mutate state; the caller decides whether to
    /// proceed with the write.
    /// </summary>
    Task<Result<QuotaCheckDto>> CheckAsync(
        Guid broadcasterId,
        string limitKey,
        long resultingCount,
        CancellationToken ct = default
    );
}
