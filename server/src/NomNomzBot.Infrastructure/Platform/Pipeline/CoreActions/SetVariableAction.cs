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

public sealed class SetVariableAction : ICommandAction
{
    public string ActionType => "set_variable";

    public LocalizedText Category => new("pipeline.category.flow");

    public LocalizedText Description => new("pipeline.set_variable.description");
    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "name",
                PipelineActionFieldKind.Text,
                Required: true,
                Description: new("pipeline.set_variable.name.help"),
                DeclaresVariable: true
            ),
            new(
                "value",
                PipelineActionFieldKind.Text,
                Templated: true,
                Description: new("pipeline.set_variable.value.help")
            ),
        ];

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
