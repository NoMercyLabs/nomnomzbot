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

/// <summary>Exits the innermost enclosing <c>loop</c> block; execution resumes after that loop
/// (pipeline-control-flow.md D3). Outside any loop it is an honest no-op — the engine decides
/// whether an enclosing loop exists to act on (<see cref="PipelineExecutionContext.LoopDepth"/>).</summary>
public sealed class BreakAction : ICommandAction
{
    public string ActionType => "break";

    public Task<ActionResult> ExecuteAsync(PipelineExecutionContext ctx, ActionDefinition action)
    {
        ctx.ShouldBreakLoop = true;
        return Task.FromResult(ActionResult.Success("Break requested"));
    }
}
