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
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Domain.CustomCode.Entities;
using NomNomzBot.Domain.CustomCode.Enums;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// Orchestrates one sandboxed script run (custom-code.md §3.5): loads the active valid version, executes it through
/// the hardened <see cref="IScriptExecutor"/>, and records <c>LastRanAt</c>/<c>LastRuntimeError</c>. Fail-closed:
/// a disabled / version-less / non-valid script never reaches the executor. (Deferred — documented: the capability
/// broker grant assembly (currently an empty grant — side-effecting bot.* is denied until the broker lands), the
/// per-tenant exec-ms meter gate, and the execution-audit events.)
/// </summary>
public sealed class ScriptRunner(
    IApplicationDbContext db,
    IScriptExecutor executor,
    IScriptCapabilityBroker broker,
    IScriptExecutionMeter meter,
    ITwitchChatService chatService,
    ITwitchIdentityResolver identityResolver,
    ICurrencyAccountService currencyService,
    TimeProvider clock
) : IScriptRunner
{
    public async Task<Result<ScriptRunResult>> RunAsync(
        Guid codeScriptId,
        ScriptInvocation invocation,
        CancellationToken cancellationToken = default
    )
    {
        CodeScript? script = await db.CodeScripts.FirstOrDefaultAsync(
            s => s.Id == codeScriptId && s.DeletedAt == null,
            cancellationToken
        );
        if (script is null)
            return Result.Failure<ScriptRunResult>("Script not found.", "NOT_FOUND");

        if (!script.IsEnabled)
            return Faulted("Script is disabled.", ScriptDenialReason.ScriptDisabled);

        // Meter-gate: refuse before execution when the tenant is over its sandbox-exec-ms budget (fail-closed).
        Result<QuotaCheck> budget = await meter.CheckSandboxBudgetAsync(
            script.BroadcasterId,
            cancellationToken
        );
        if (budget.IsSuccess && !budget.Value.Allowed)
            return Result.Success(
                new ScriptRunResult(
                    ScriptExecutionOutcome.Denied,
                    new Dictionary<string, string>(),
                    Output: null,
                    StopPipeline: false,
                    ErrorMessage: "Sandbox execution quota exhausted for this period.",
                    DenialReason: ScriptDenialReason.QuotaExceeded
                )
            );

        CodeScriptVersion? version = script.CurrentVersionId is Guid versionId
            ? await db.CodeScriptVersions.FirstOrDefaultAsync(
                v => v.Id == versionId,
                cancellationToken
            )
            : null;
        if (version is null || version.ValidationStatus != "valid" || version.CompiledJs is null)
            return Faulted(
                "Script has no valid published version.",
                ScriptDenialReason.VersionInvalid
            );

        ScriptExecutionRequest request = new(
            invocation.ExecutionId,
            version.CompiledJs,
            version.CompiledHash ?? string.Empty,
            new ScriptInputs(
                invocation.TriggeredByUserId,
                invocation.TriggeredByDisplayName,
                invocation.Args,
                invocation.Variables
            ),
            ScriptResourceBudget.Baseline
        );
        // Deny-by-default grant: the broker validates the version's declared capabilities against the catalogue
        // + feature-flag gates. A disallowed capability denies the whole run (fail-closed).
        List<string> declared =
            JsonConvert.DeserializeObject<List<string>>(version.DeclaredCapabilitiesJson) ?? [];
        Result<ScriptCapabilityGrant> grantResult = await broker.BuildGrantAsync(
            script.BroadcasterId,
            declared,
            cancellationToken
        );
        if (grantResult.IsFailure)
        {
            script.LastRanAt = clock.GetUtcNow().UtcDateTime;
            script.LastRuntimeError = grantResult.ErrorMessage;
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success(
                new ScriptRunResult(
                    ScriptExecutionOutcome.Denied,
                    new Dictionary<string, string>(),
                    Output: null,
                    StopPipeline: false,
                    ErrorMessage: grantResult.ErrorMessage,
                    DenialReason: ScriptDenialReason.CapabilityDenied
                )
            );
        }

        ScriptHostBridge bridge = new(
            script.BroadcasterId,
            invocation.TriggeredByUserId,
            chatService,
            identityResolver,
            currencyService
        );
        Result<ScriptExecutionOutcomeResult> executed = await executor.ExecuteAsync(
            request,
            grantResult.Value,
            bridge,
            cancellationToken
        );
        ScriptExecutionOutcomeResult outcome = executed.Value;

        script.LastRanAt = clock.GetUtcNow().UtcDateTime;
        script.LastRuntimeError =
            outcome.Outcome == ScriptExecutionOutcome.Success ? null : outcome.ErrorMessage;
        await db.SaveChangesAsync(cancellationToken);

        // Meter-record: accumulate this run's elapsed ms into the tenant's period usage.
        await meter.RecordSandboxUsageAsync(
            script.BroadcasterId,
            outcome.ElapsedMs,
            invocation.ExecutionId,
            cancellationToken
        );

        ScriptDenialReason? denial =
            outcome.Outcome == ScriptExecutionOutcome.Denied
                ? ScriptDenialReason.CapabilityDenied
                : null;
        return Result.Success(
            new ScriptRunResult(
                outcome.Outcome,
                outcome.VariablesOut,
                outcome.ChatOutput,
                outcome.StopPipeline,
                outcome.ErrorMessage,
                denial
            )
        );
    }

    private static Result<ScriptRunResult> Faulted(string message, ScriptDenialReason reason) =>
        Result.Success(
            new ScriptRunResult(
                ScriptExecutionOutcome.Faulted,
                new Dictionary<string, string>(),
                Output: null,
                StopPipeline: false,
                ErrorMessage: message,
                DenialReason: reason
            )
        );
}
