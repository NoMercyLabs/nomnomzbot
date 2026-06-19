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

public sealed class SetVariableAction : ICommandAction
{
    public string ActionType => "set_variable";

    public Task<ActionResult> ExecuteAsync(PipelineExecutionContext ctx, ActionDefinition action)
    {
        string? name = action.GetString("name");
        string value = action.GetString("value") ?? string.Empty;

        if (string.IsNullOrEmpty(name))
            return Task.FromResult(ActionResult.Failure("set_variable requires 'name'"));

        ctx.Variables[name] = value;
        return Task.FromResult(ActionResult.Success($"{name}={value}"));
    }
}
