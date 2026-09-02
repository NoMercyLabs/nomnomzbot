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
using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>
/// Builds the wire-shape pipeline graph (<c>steps[].action</c> / <c>steps[].condition</c>) from normalized
/// <see cref="PipelineStep"/> rows — the same shape <see cref="PipelineDefinition"/> reads back. The
/// <see cref="Pipeline.GraphJsonCache"/> column is only a performance cache regenerated from these rows at
/// write time; the rows themselves are the execution truth. Shared by <c>PipelineService</c> (the editor's
/// GET fallback) and <c>ChannelRegistry</c> (the chat hot-path loader) so both read the SAME reconstruction
/// instead of one trusting a cache column that can silently drift from the rows it was built from.
/// </summary>
public static class PipelineGraphBuilder
{
    public static JsonElement BuildGraph(IReadOnlyList<PipelineStep> steps)
    {
        List<object> stepNodes = [];
        foreach (PipelineStep step in steps)
        {
            JsonElement actionJson;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(step.ConfigJson);
                Dictionary<string, JsonElement> actionFields = doc
                    .RootElement.EnumerateObject()
                    .Where(prop =>
                        !string.Equals(prop.Name, "type", StringComparison.OrdinalIgnoreCase)
                    )
                    .ToDictionary(prop => prop.Name, prop => prop.Value.Clone());
                actionFields["type"] = JsonSerializer.SerializeToElement(step.ActionType);
                actionJson = JsonSerializer.SerializeToElement(actionFields);
            }
            catch (JsonException)
            {
                actionJson = JsonSerializer.SerializeToElement(new { type = step.ActionType });
            }

            PipelineStepCondition? firstCondition = step
                .Conditions.OrderBy(c => c.Order)
                .FirstOrDefault();

            object? conditionNode = firstCondition is null
                ? null
                : new
                {
                    type = firstCondition.ConditionType,
                    @operator = firstCondition.Operator ?? "eq",
                    left = firstCondition.LeftOperand ?? string.Empty,
                    right = firstCondition.RightOperand ?? string.Empty,
                    negate = firstCondition.Negate,
                };

            JsonElement? blockConfigNode = null;
            if (!string.IsNullOrEmpty(step.BlockConfigJson))
            {
                try
                {
                    using JsonDocument blockDoc = JsonDocument.Parse(step.BlockConfigJson);
                    blockConfigNode = blockDoc.RootElement.Clone();
                }
                catch (JsonException)
                {
                    blockConfigNode = null;
                }
            }

            stepNodes.Add(
                new
                {
                    id = step.Id.ToString(),
                    parent_step_id = step.ParentStepId?.ToString(),
                    branch = step.Branch,
                    block_kind = step.BlockKind,
                    block_config = blockConfigNode,
                    order = step.Order,
                    action = actionJson,
                    condition = conditionNode,
                    continue_on_error = step.ContinueOnError,
                }
            );
        }

        return JsonSerializer.SerializeToElement(new { steps = stepNodes });
    }

    public static string BuildGraphJson(IReadOnlyList<PipelineStep> steps) =>
        JsonSerializer.Serialize(BuildGraph(steps));
}
