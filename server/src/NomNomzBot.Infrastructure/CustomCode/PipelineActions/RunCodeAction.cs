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

using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.CustomCode.Enums;
using NomNomzBot.Domain.Platform;

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

    public LocalizedText Category => new("pipeline.category.code");

    public LocalizedText Description => new("pipeline.run_code.description");
    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "code_script_id",
                PipelineActionFieldKind.Script,
                Required: true,
                Description: new("pipeline.run_code.code_script_id.help")
            ),
        ];

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!OwnedIdCodec.TryDecode(action.GetString("code_script_id"), out Guid codeScriptId))
            return ActionResult.Failure("run_code requires a valid code_script_id.");

        ScriptInvocation invocation = ScriptInvocationFactory.Create(
            ctx.ExecutionId,
            ctx.TriggeredByUserId,
            ctx.TriggeredByDisplayName,
            ScriptInvocationFactory.ArgsAfterCommand(ctx.RawMessage),
            ctx.Variables,
            ctx.MessageId,
            ctx.TriggeredByChatName
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

        bool succeeded = run.Outcome == ScriptExecutionOutcome.Success;
        return new()
        {
            Succeeded = succeeded,
            Output = succeeded ? run.Output : null,
            ErrorMessage = succeeded
                ? null
                : run.ErrorMessage ?? "Script did not complete successfully.",
            FailureShape = succeeded ? null : run.FailureShape,
            HostCalls = run.HostCalls ?? [],
        };
    }
}
