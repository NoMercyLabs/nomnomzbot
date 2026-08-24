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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.Commands;

public class PipelineTreeCompatibilityService : IPipelineTreeCompatibilityService
{
    private sealed record CommandTriggerConfig(
        string Name,
        List<string>? Aliases,
        string PrefixMode,
        string? CustomPrefix,
        string MatchMode,
        string? MatchPattern
    );

    public IReadOnlyList<PipelineStepCondition> UpcastConditionTree(
        IReadOnlyList<PipelineStepCondition> flatConditions
    )
    {
        if (flatConditions.Count == 0)
            return [];

        bool alreadyTreeShaped = flatConditions.Any(c =>
            c.ParentConditionId is not null || c.GroupOp is not null
        );
        if (alreadyTreeShaped)
            return [.. flatConditions.OrderBy(c => c.Order)];

        Guid syntheticRootId = Guid.Empty;

        PipelineStepCondition syntheticRoot = new()
        {
            Id = syntheticRootId,
            PipelineStepId = flatConditions[0].PipelineStepId,
            BroadcasterId = flatConditions[0].BroadcasterId,
            ParentConditionId = null,
            GroupOp = "and",
            ConditionType = "",
            Order = 0,
        };

        List<PipelineStepCondition> children =
        [
            .. flatConditions
                .OrderBy(c => c.Order)
                .Select(c => new PipelineStepCondition
                {
                    Id = c.Id,
                    PipelineStepId = c.PipelineStepId,
                    BroadcasterId = c.BroadcasterId,
                    ParentConditionId = syntheticRootId,
                    GroupOp = null,
                    ConditionType = c.ConditionType,
                    Operator = c.Operator,
                    LeftOperand = c.LeftOperand,
                    RightOperand = c.RightOperand,
                    Negate = c.Negate,
                    Order = c.Order,
                }),
        ];

        return [syntheticRoot, .. children];
    }

    public IReadOnlyList<PipelineTrigger> UpcastTriggers(
        Pipeline pipeline,
        Command? wrappingCommand
    )
    {
        if (pipeline.Triggers.Count > 0)
            return [.. pipeline.Triggers.OrderBy(t => t.Order)];

        string configJson = "{}";
        if (pipeline.TriggerKind == "command" && wrappingCommand is not null)
        {
            configJson = JsonSerializer.Serialize(
                new CommandTriggerConfig(
                    wrappingCommand.Name,
                    wrappingCommand.Aliases,
                    wrappingCommand.PrefixMode,
                    wrappingCommand.CustomPrefix,
                    wrappingCommand.MatchMode,
                    wrappingCommand.MatchPattern
                )
            );
        }

        PipelineTrigger synthetic = new()
        {
            Id = Guid.Empty,
            PipelineId = pipeline.Id,
            BroadcasterId = pipeline.BroadcasterId,
            Kind = pipeline.TriggerKind,
            Order = 0,
            ConfigJson = configJson,
            IsEnabled = true,
        };

        return [synthetic];
    }

    public IReadOnlyList<PipelineStep> UpcastStepTree(IReadOnlyList<PipelineStep> steps)
    {
        return [.. DepthFirst(steps, parentStepId: null)];
    }

    private static IEnumerable<PipelineStep> DepthFirst(
        IReadOnlyList<PipelineStep> steps,
        Guid? parentStepId
    )
    {
        IEnumerable<PipelineStep> siblings = steps
            .Where(s => s.ParentStepId == parentStepId)
            .OrderBy(s => s.Order);

        foreach (PipelineStep step in siblings)
        {
            yield return step;
            foreach (PipelineStep descendant in DepthFirst(steps, step.Id))
                yield return descendant;
        }
    }
}
