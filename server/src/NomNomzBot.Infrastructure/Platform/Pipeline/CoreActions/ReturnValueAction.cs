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

namespace NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;

/// <summary>Sets the current run's return value, read by a <c>run_pipeline inline</c> caller into
/// <c>{{call.result}}</c> (pipeline-tree-and-editor.md §2.5). Implicitly also <c>stop</c>s the
/// pipeline — a return ends execution, matching every language's <c>return</c> semantics and
/// sparing an author a trailing <c>stop</c> they'd otherwise forget. <c>value</c> is templated
/// (S-PIPE-TREE-d2b(b)) — resolved centrally by the pipeline engine's leaf executor before
/// <see cref="ExecuteAsync"/> runs, same as <see cref="SetVariableAction"/>'s <c>value</c>.</summary>
public sealed class ReturnValueAction : ICommandAction
{
    public string ActionType => "return_value";

    public string Category => "flow";

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [new("value", PipelineActionFieldKind.Text, Templated: true)];

    public Task<ActionResult> ExecuteAsync(PipelineExecutionContext ctx, ActionDefinition action)
    {
        string value = action.GetString("value") ?? string.Empty;
        ctx.ReturnValue = value;
        ctx.ShouldStop = true;
        return Task.FromResult(ActionResult.Success(value));
    }
}
