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
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.DTOs.Billing;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// The sandbox <c>sandbox_exec_ms</c> quota gate + meter (custom-code.md §3.3) — a thin adapter over the billing
/// usage meter (the single owner of <c>TierLimit</c> resolution + the <c>UsageRecord</c> increment). Self-host is
/// unlimited (billing no-ops). (Deferred — documented: per-ExecutionId idempotency; the billing record accumulates,
/// so a double-call would double-count — the runner calls it once per run.)
/// </summary>
public sealed class ScriptExecutionMeter(IUsageMeteringService usage, TimeProvider clock)
    : IScriptExecutionMeter
{
    private const string MetricKey = "sandbox_exec_ms";

    public async Task<Result<QuotaCheck>> CheckSandboxBudgetAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        Result<QuotaCheckDto> check = await usage.CheckAsync(
            broadcasterId,
            MetricKey,
            0,
            cancellationToken
        );
        if (check.IsFailure)
            return Result.Failure<QuotaCheck>(
                check.ErrorMessage ?? "Quota check failed.",
                check.ErrorCode ?? "ERROR"
            );

        DateTime now = clock.GetUtcNow().UtcDateTime;
        DateTime periodStart = new(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return Result.Success(
            new QuotaCheck(
                check.Value.Allowed,
                check.Value.Limit,
                check.Value.Used,
                periodStart,
                periodStart.AddMonths(1)
            )
        );
    }

    public Task<Result> RecordSandboxUsageAsync(
        Guid broadcasterId,
        long elapsedMs,
        string executionId,
        CancellationToken cancellationToken = default
    ) =>
        elapsedMs <= 0
            ? Task.FromResult(Result.Success())
            : usage.RecordAsync(broadcasterId, MetricKey, elapsedMs, cancellationToken);
}
