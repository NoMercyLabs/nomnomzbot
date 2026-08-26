// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;

namespace NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;

/// <summary>Skips the remaining steps of the current iteration of the innermost enclosing <c>loop</c>
/// block; that loop then starts its next iteration (pipeline-control-flow.md D3). Outside any loop it
/// is an honest no-op — the engine decides whether an enclosing loop exists to act on
/// (<see cref="PipelineExecutionContext.LoopDepth"/>).</summary>
public sealed class ContinueAction : ICommandAction
{
    public string ActionType => "continue";

    public LocalizedText Category => new("pipeline.category.flow");

    public LocalizedText Description => new("pipeline.continue.description");

    public Task<ActionResult> ExecuteAsync(PipelineExecutionContext ctx, ActionDefinition action)
    {
        ctx.ShouldContinueLoop = true;
        return Task.FromResult(ActionResult.Success("Continue requested"));
    }
}
