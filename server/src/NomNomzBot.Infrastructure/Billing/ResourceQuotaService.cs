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
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Billing;

namespace NomNomzBot.Infrastructure.Billing;

/// <summary>
/// Resolves a limit lever from <see cref="LimitedResourceRegistry"/> and evaluates it (S-BUDGETS-a). NEAR_FREE
/// keys are checked against the registry's own safety baseline — the same value for every tenant, self-host
/// included, so a self-host deployment is never crippled below the abuse floor but also never sold headroom on
/// a free-to-serve resource. COST_DRIVING keys delegate to <see cref="IBillingTierService"/>, which already
/// resolves self-host to unlimited.
/// </summary>
public sealed class ResourceQuotaService(IBillingTierService tiers) : IResourceQuotaService
{
    public async Task<Result<QuotaCheckDto>> CheckAsync(
        Guid broadcasterId,
        string limitKey,
        long resultingCount,
        CancellationToken ct = default
    )
    {
        if (!LimitedResourceRegistry.TryGet(limitKey, out LimitedResourceDescriptor descriptor))
            return Result.Failure<QuotaCheckDto>(
                $"'{limitKey}' is not a declared limited resource.",
                "NOT_FOUND"
            );

        long limit =
            descriptor.Class == ResourceClass.NearFree
                ? descriptor.SafetyBaseline
                : (await tiers.GetLimitAsync(broadcasterId, limitKey, ct)).Value;

        bool allowed = limit == -1 || resultingCount <= limit;
        long remaining = limit == -1 ? -1 : Math.Max(0, limit - resultingCount);

        return Result.Success(
            new QuotaCheckDto(allowed, limitKey, resultingCount, limit, remaining)
        );
    }
}
