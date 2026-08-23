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
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Billing.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Billing;

/// <summary>
/// Cost-driver metering + quota enforcement (monetization-billing.md §3.3). Counters accumulate into the single
/// current-period <c>UsageRecord</c> per <c>(channel, metric, calendar month)</c>; self-host is a no-op (the
/// limit resolves to unlimited). (Deferred — documented: <see cref="ReportUnbilledUsageToStripeAsync"/> awaits
/// the Stripe gateway; it is a safe no-op until then.)
/// </summary>
public sealed class UsageMeteringService(
    IApplicationDbContext db,
    IBillingTierService tiers,
    IEventBus eventBus,
    TimeProvider clock
) : IUsageMeteringService
{
    public async Task<Result> RecordAsync(
        Guid broadcasterId,
        string metricKey,
        long quantity,
        CancellationToken ct = default
    )
    {
        if (quantity <= 0)
            return Result.Failure("Quantity must be positive.", "VALIDATION_FAILED");

        long limit = (await tiers.GetLimitAsync(broadcasterId, metricKey, ct)).Value;
        if (limit == -1)
            return Result.Success(); // unlimited (self-host or unmetered key) — nothing to meter

        (DateTime periodStart, DateTime periodEnd) = CurrentMonth();

        // Best-effort snapshot for the quota-crossing check below — not load-bearing for correctness of the
        // persisted total (the increment itself is atomic, see IncrementExistingAsync/insert-race retry).
        long before = await CurrentQuantityAsync(broadcasterId, metricKey, periodStart, ct);

        int rowsUpdated = await IncrementExistingAsync(
            broadcasterId,
            metricKey,
            periodStart,
            quantity,
            ct
        );

        if (rowsUpdated == 0)
        {
            // No current-period row yet. Insert one; a concurrent caller racing this same first-of-the-month
            // increment will lose the unique-index race and fold its quantity into the winner's row atomically
            // instead of throwing or silently dropping its units (S004/S004b mechanism: guard the write against
            // the CURRENT row, not a prior in-memory read).
            UsageRecord record = new()
            {
                BroadcasterId = broadcasterId,
                MetricKey = metricKey,
                Quantity = quantity,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                CreatedAt = clock.GetUtcNow().UtcDateTime,
            };
            db.UsageRecords.Add(record);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                if (db is DbContext context)
                    context.Entry(record).State = EntityState.Detached;
                int retried = await IncrementExistingAsync(
                    broadcasterId,
                    metricKey,
                    periodStart,
                    quantity,
                    ct
                );
                if (retried == 0)
                    return Result.Failure(
                        "Failed to record usage after an insert-race retry.",
                        "USAGE_RECORD_RACE"
                    );
            }
        }

        long after = await CurrentQuantityAsync(broadcasterId, metricKey, periodStart, ct);

        // Fire once, on the first crossing of the limit this period.
        if (before < limit && after >= limit)
            await eventBus.PublishAsync(
                new UsageQuotaExceededEvent
                {
                    BroadcasterId = broadcasterId,
                    MetricKey = metricKey,
                    Used = after,
                    Limit = limit,
                    PeriodStart = new(periodStart, TimeSpan.Zero),
                    PeriodEnd = new(periodEnd, TimeSpan.Zero),
                },
                ct
            );
        return Result.Success();
    }

    // Atomic `SET Quantity = Quantity + quantity` evaluated against the CURRENT row at write time — the same
    // ExecuteUpdateAsync-with-guard mechanism as CurrencyAccountService.AppendAsync (S004/S004b). Returns the
    // number of rows updated (0 means no current-period row exists yet).
    private Task<int> IncrementExistingAsync(
        Guid broadcasterId,
        string metricKey,
        DateTime periodStart,
        long quantity,
        CancellationToken ct
    ) =>
        db
            .UsageRecords.Where(u =>
                u.BroadcasterId == broadcasterId
                && u.MetricKey == metricKey
                && u.PeriodStart == periodStart
            )
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(u => u.Quantity, u => u.Quantity + quantity),
                ct
            );

    private Task<long> CurrentQuantityAsync(
        Guid broadcasterId,
        string metricKey,
        DateTime periodStart,
        CancellationToken ct
    ) =>
        db
            .UsageRecords.Where(u =>
                u.BroadcasterId == broadcasterId
                && u.MetricKey == metricKey
                && u.PeriodStart == periodStart
            )
            .Select(u => u.Quantity)
            .FirstOrDefaultAsync(ct);

    public async Task<Result<QuotaCheckDto>> CheckAsync(
        Guid broadcasterId,
        string metricKey,
        long requestedQuantity,
        CancellationToken ct = default
    )
    {
        long limit = (await tiers.GetLimitAsync(broadcasterId, metricKey, ct)).Value;
        (DateTime periodStart, _) = CurrentMonth();
        long used = await db
            .UsageRecords.Where(u =>
                u.BroadcasterId == broadcasterId
                && u.MetricKey == metricKey
                && u.PeriodStart == periodStart
            )
            .Select(u => u.Quantity)
            .FirstOrDefaultAsync(ct);

        bool allowed = limit == -1 || used + requestedQuantity <= limit;
        long remaining = limit == -1 ? long.MaxValue : Math.Max(0, limit - used);
        return Result.Success(new QuotaCheckDto(allowed, metricKey, used, limit, remaining));
    }

    public async Task<Result<IReadOnlyList<UsageMetricDto>>> GetCurrentUsageAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result<EntitlementDto> entitlement = await tiers.GetEntitlementAsync(broadcasterId, ct);
        if (entitlement.IsFailure)
            return Result.Failure<IReadOnlyList<UsageMetricDto>>(
                entitlement.ErrorMessage,
                entitlement.ErrorCode
            );

        (DateTime periodStart, DateTime periodEnd) = CurrentMonth();
        Dictionary<string, long> usedByMetric = await db
            .UsageRecords.Where(u =>
                u.BroadcasterId == broadcasterId && u.PeriodStart == periodStart
            )
            .ToDictionaryAsync(u => u.MetricKey, u => u.Quantity, ct);

        DateTimeOffset start = new(periodStart, TimeSpan.Zero);
        DateTimeOffset end = new(periodEnd, TimeSpan.Zero);
        return Result.Success<IReadOnlyList<UsageMetricDto>>([
            .. entitlement.Value.Limits.Select(kv =>
            {
                long used = usedByMetric.GetValueOrDefault(kv.Key, 0);
                long remaining = kv.Value == -1 ? long.MaxValue : Math.Max(0, kv.Value - used);
                return new UsageMetricDto(kv.Key, used, kv.Value, remaining, start, end);
            }),
        ]);
    }

    public Task<Result<int>> ReportUnbilledUsageToStripeAsync(CancellationToken ct = default) =>
        // Stripe metered-billing flush is deferred to the Stripe gateway; a safe no-op until then.
        Task.FromResult(Result.Success(0));

    private (DateTime Start, DateTime End) CurrentMonth()
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        DateTime start = new(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return (start, start.AddMonths(1));
    }
}
