// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.Platform.Pipeline;

/// <summary>
/// Turns an unsaved graph (the draft a person is editing) into the in-memory <see cref="PipelineStep"/> rows the
/// tree walker runs, so a draft with blocks runs through the same engine path as a saved tree pipeline. The rows
/// are never added to a database context. A step id that is a GUID is kept, so the run trace names each step by
/// the id the client sent; any other id gets a fresh one. Mirrors how a save maps the graph to rows.
/// </summary>
internal static class PipelineDraftRows
{
    /// <summary>True when any step is a block or sits inside one, so the graph needs the tree walker.</summary>
    public static bool IsTree(PipelineDefinition definition) =>
        definition.Steps.Any(step =>
            !string.IsNullOrEmpty(step.BlockKind) || !string.IsNullOrEmpty(step.ParentStepId)
        );

    public static List<PipelineStep> Build(PipelineDefinition definition, Guid broadcasterId)
    {
        Guid pipelineId = Guid.NewGuid();
        Guid[] ids = [.. definition.Steps.Select(step => ResolveId(step.Id))];
        Dictionary<string, Guid> idMap = new(StringComparer.Ordinal);
        for (int i = 0; i < definition.Steps.Count; i++)
        {
            if (!string.IsNullOrEmpty(definition.Steps[i].Id))
                idMap[definition.Steps[i].Id!] = ids[i];
        }

        List<PipelineStep> rows = [];
        for (int i = 0; i < definition.Steps.Count; i++)
        {
            PipelineStepDefinition stepDef = definition.Steps[i];
            Guid? parentStepId =
                !string.IsNullOrEmpty(stepDef.ParentStepId)
                && idMap.TryGetValue(stepDef.ParentStepId!, out Guid mappedParentId)
                    ? mappedParentId
                    : null;

            rows.Add(
                new()
                {
                    Id = ids[i],
                    PipelineId = pipelineId,
                    BroadcasterId = broadcasterId,
                    ParentStepId = parentStepId,
                    Branch = stepDef.Branch,
                    BlockKind = stepDef.BlockKind,
                    BlockConfigJson = stepDef.BlockConfig?.GetRawText(),
                    Order = stepDef.Order ?? i,
                    ContinueOnError = stepDef.ContinueOnError,
                    StopOnMatch = stepDef.StopOnMatch && stepDef.Condition is not null,
                    ActionType = stepDef.Action.Type,
                    ConfigJson = JsonSerializer.Serialize(
                        stepDef.Action.Parameters ?? new Dictionary<string, JsonElement>()
                    ),
                    IsEnabled = true,
                    Conditions = ToConditions(stepDef.Condition, ids[i], broadcasterId),
                }
            );
        }

        return rows;
    }

    private static Guid ResolveId(string? clientId) =>
        Guid.TryParse(clientId, out Guid parsed) ? parsed : Guid.NewGuid();

    private static List<PipelineStepCondition> ToConditions(
        ConditionDefinition? condition,
        Guid stepId,
        Guid broadcasterId
    ) =>
        condition is null
            ? []
            :
            [
                new()
                {
                    Id = Guid.NewGuid(),
                    PipelineStepId = stepId,
                    BroadcasterId = broadcasterId,
                    ConditionType = condition.Type,
                    Operator = condition.GetString("operator") ?? "eq",
                    LeftOperand = condition.GetString("left") ?? string.Empty,
                    RightOperand = condition.GetString("right") ?? string.Empty,
                    Negate = string.Equals(
                        condition.GetString("negate"),
                        "true",
                        StringComparison.OrdinalIgnoreCase
                    ),
                    Order = 0,
                },
            ];
}
