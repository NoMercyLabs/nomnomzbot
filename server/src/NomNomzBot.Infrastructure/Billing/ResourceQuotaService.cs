// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
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
public sealed class ResourceQuotaService(
    IBillingTierService tiers,
    IUsageMeteringService metering,
    IApplicationDbContext db
) : IResourceQuotaService
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

    public Task<Result<long>> GetCurrentCountAsync(
        Guid broadcasterId,
        string limitKey,
        CancellationToken ct = default
    ) =>
        limitKey switch
        {
            "custom_commands" => CountAsync(
                db.Commands.Where(c => c.BroadcasterId == broadcasterId),
                ct
            ),
            "timers" => CountAsync(db.Timers.Where(t => t.BroadcasterId == broadcasterId), ct),
            "event_responses" => CountAsync(
                db.EventResponses.Where(e => e.BroadcasterId == broadcasterId && e.IsEnabled),
                ct
            ),
            _ => Task.FromResult(
                Result.Failure<long>(
                    $"'{limitKey}' has no single broadcaster-wide current-count source.",
                    "NOT_SUPPORTED"
                )
            ),
        };

    private static async Task<Result<long>> CountAsync<T>(
        IQueryable<T> query,
        CancellationToken ct
    ) => Result.Success((long)await query.CountAsync(ct));

    public async Task<Result<IReadOnlyList<ResourceUsageDto>>> GetUsageReportAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result<IReadOnlyList<UsageMetricDto>> usageResult = await metering.GetCurrentUsageAsync(
            broadcasterId,
            ct
        );
        if (usageResult.IsFailure)
            return Result.Failure<IReadOnlyList<ResourceUsageDto>>(
                usageResult.ErrorMessage,
                usageResult.ErrorCode
            );
        Dictionary<string, UsageMetricDto> costDrivingByKey = usageResult.Value.ToDictionary(
            u => u.MetricKey,
            StringComparer.Ordinal
        );

        List<ResourceUsageDto> report = [];
        foreach (LimitedResourceDescriptor descriptor in LimitedResourceRegistry.Resources)
        {
            if (descriptor.Class == ResourceClass.NearFree)
            {
                Result<long> countResult = await GetCurrentCountAsync(
                    broadcasterId,
                    descriptor.LimitKey,
                    ct
                );
                if (countResult.IsFailure)
                    continue; // no channel-wide aggregate for this key (e.g. per-trigger variation cap)
                report.Add(
                    new ResourceUsageDto(
                        descriptor.LimitKey,
                        descriptor.Class,
                        descriptor.DisplayName,
                        countResult.Value,
                        descriptor.SafetyBaseline,
                        descriptor.SafetyBaseline
                    )
                );
            }
            else if (costDrivingByKey.TryGetValue(descriptor.LimitKey, out UsageMetricDto? usage))
            {
                report.Add(
                    new ResourceUsageDto(
                        descriptor.LimitKey,
                        descriptor.Class,
                        descriptor.DisplayName,
                        usage.Used,
                        usage.Limit,
                        SafetyBaseline: 0
                    )
                );
            }
            else
            {
                // Not yet metered this period (no UsageRecord row) — current usage is truthfully zero; the
                // limit still comes from the tier so self-host reports unlimited (-1), never a paid ceiling.
                long limit = (
                    await tiers.GetLimitAsync(broadcasterId, descriptor.LimitKey, ct)
                ).Value;
                report.Add(
                    new ResourceUsageDto(
                        descriptor.LimitKey,
                        descriptor.Class,
                        descriptor.DisplayName,
                        CurrentCount: 0,
                        limit,
                        SafetyBaseline: 0
                    )
                );
            }
        }

        return Result.Success<IReadOnlyList<ResourceUsageDto>>(report);
    }
}
