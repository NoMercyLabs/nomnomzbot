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

namespace NomNomzBot.Application.Contracts.CustomCode;

/// <summary>
/// The sandbox <c>sandbox_exec_ms</c> quota gate + usage meter (custom-code.md §3.3). Pre-run: refuse a run when the
/// tenant is over its period budget (fail-closed). Post-run: accumulate elapsed ms into the period usage. Self-host
/// is unlimited (a no-op success).
/// </summary>
public interface IScriptExecutionMeter
{
    Task<Result<QuotaCheck>> CheckSandboxBudgetAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );

    Task<Result> RecordSandboxUsageAsync(
        Guid broadcasterId,
        long elapsedMs,
        string executionId,
        CancellationToken cancellationToken = default
    );
}
