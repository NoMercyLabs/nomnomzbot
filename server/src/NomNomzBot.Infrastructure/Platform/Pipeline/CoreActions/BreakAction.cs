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

namespace NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;

/// <summary>Exits the innermost enclosing <c>loop</c> block; execution resumes after that loop
/// (pipeline-control-flow.md D3). Outside any loop it is an honest no-op — the engine decides
/// whether an enclosing loop exists to act on (<see cref="PipelineExecutionContext.LoopDepth"/>).</summary>
public sealed class BreakAction : ICommandAction
{
    public string ActionType => "break";

    public LocalizedText Category => new("pipeline.category.flow");

    public LocalizedText Description => new("pipeline.break.description");

    public Task<ActionResult> ExecuteAsync(PipelineExecutionContext ctx, ActionDefinition action)
    {
        ctx.ShouldBreakLoop = true;
        return Task.FromResult(ActionResult.Success("Break requested"));
    }
}
