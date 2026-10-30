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
using NomNomzBot.Application.Contracts.CustomCode;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// The one rule for a plan without code scripts: a <c>sandbox_exec_ms</c> limit of 0 means the plan has none,
/// so a run is refused before it starts instead of asking the meter for 0 ms (which always fits).
/// </summary>
internal static class ScriptPlanGate
{
    /// <summary>The refusal a channel on such a plan gets.</summary>
    public static readonly Result Refusal = Errors.PlanRequired("Code scripts", "Base");

    public static bool IsExcluded(QuotaCheck budget) => budget.LimitMs == 0;

    /// <summary>
    /// The refusal for a script write (create, save, publish, turn on) on a plan without code scripts, or null when
    /// the plan allows scripts. A budget that cannot be read does not refuse a write: the run gate stays fail-closed,
    /// so nothing unmetered ever runs.
    /// </summary>
    public static async Task<Result?> RefuseWriteAsync(
        IScriptExecutionMeter meter,
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        Result<QuotaCheck> budget = await meter.CheckSandboxBudgetAsync(
            broadcasterId,
            cancellationToken
        );
        return budget.IsSuccess && IsExcluded(budget.Value) ? Refusal : null;
    }
}
