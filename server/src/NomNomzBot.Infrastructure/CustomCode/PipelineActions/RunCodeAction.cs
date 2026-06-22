// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.CustomCode.Enums;

namespace NomNomzBot.Infrastructure.CustomCode.PipelineActions;

/// <summary>
/// Pipeline action <c>run_code</c> (custom-code.md §6) — the ONLY path that executes a sandboxed script. Resolves
/// the bound <c>code_script_id</c>, runs it through <see cref="IScriptRunner"/> with the step's args + variables,
/// merges the script's variable writes back into the pipeline, surfaces its chat output, and honors its stop flag.
/// Fail-closed: a missing id or a non-success outcome fails the step.
/// </summary>
public sealed class RunCodeAction(IScriptRunner runner) : ICommandAction
{
    public string ActionType => "run_code";

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!Guid.TryParse(action.GetString("code_script_id"), out Guid codeScriptId))
            return ActionResult.Failure("run_code requires a valid code_script_id.");

        IReadOnlyList<string> args =
        [
            .. ctx.RawMessage.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Skip(1),
        ];
        ScriptInvocation invocation = new(
            ctx.ExecutionId,
            ctx.TriggeredByUserId,
            ctx.TriggeredByDisplayName,
            args,
            new Dictionary<string, string>(ctx.Variables)
        );

        Result<ScriptRunResult> result = await runner.RunAsync(
            codeScriptId,
            invocation,
            ctx.CancellationToken
        );
        if (result.IsFailure)
            return ActionResult.Failure(result.ErrorMessage ?? "run_code failed.");

        ScriptRunResult run = result.Value;
        foreach (KeyValuePair<string, string> variable in run.VariablesOut)
            ctx.Variables[variable.Key] = variable.Value;
        if (run.StopPipeline)
            ctx.ShouldStop = true;

        return run.Outcome == ScriptExecutionOutcome.Success
            ? ActionResult.Success(run.Output)
            : ActionResult.Failure(run.ErrorMessage ?? "Script did not complete successfully.");
    }
}
