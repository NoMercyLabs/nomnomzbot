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

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// Stands in for the billing side of the sandbox meter: the channel's plan allows <see cref="LimitMs"/> sandbox
/// milliseconds a month. -1 is unlimited (the default); 0 is a plan without code scripts, like Free.
/// </summary>
internal sealed class ScriptBudget : IScriptExecutionMeter
{
    public long LimitMs { get; set; } = -1;

    public Task<Result<QuotaCheck>> CheckSandboxBudgetAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            Result.Success(new QuotaCheck(true, LimitMs, 0, DateTime.UnixEpoch, DateTime.UnixEpoch))
        );

    public Task<Result> RecordSandboxUsageAsync(
        Guid broadcasterId,
        long elapsedMs,
        string executionId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success());
}
