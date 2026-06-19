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

public sealed class StopAction : ICommandAction
{
    public string ActionType => "stop";

    public Task<ActionResult> ExecuteAsync(PipelineExecutionContext ctx, ActionDefinition action)
    {
        ctx.ShouldStop = true;
        return Task.FromResult(ActionResult.Success("Pipeline stopped"));
    }
}
