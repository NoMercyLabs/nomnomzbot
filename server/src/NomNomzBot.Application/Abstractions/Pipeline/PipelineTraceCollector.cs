// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.CustomCode;

namespace NomNomzBot.Application.Abstractions.Pipeline;

/// <summary>
/// Collects one <see cref="PipelineTraceStepDto"/> per executed step of a pipeline TEST run. It rides on
/// <see cref="PipelineExecutionContext.Trace"/>, which is null on every normal run, so a live run pays nothing.
/// </summary>
public sealed class PipelineTraceCollector
{
    private readonly List<PipelineTraceStepDto> _steps = [];

    public IReadOnlyList<PipelineTraceStepDto> Steps => _steps;

    /// <summary>Copies the author's variables so a later <see cref="RecordLeaf"/> can diff against them.</summary>
    public static Dictionary<string, string> Snapshot(Dictionary<string, string> variables) =>
        new(variables, StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds the row for a leaf (action) step from the log it just wrote.</summary>
    public void RecordLeaf(
        string stepId,
        StepExecutionLog log,
        Dictionary<string, string> before,
        Dictionary<string, string> after
    ) =>
        _steps.Add(
            new()
            {
                StepId = stepId,
                StepType = log.ActionType,
                VariableChanges = Diff(before, after),
                Output = log.Output,
                Error = log.ErrorMessage,
            }
        );

    /// <summary>Adds the row for a block step (<c>if</c>, <c>loop</c>) and returns it so the block can update it.</summary>
    public PipelineTraceStepDto BeginBlock(string stepId, string kind, string? branch = null)
    {
        PipelineTraceStepDto row = new()
        {
            StepId = stepId,
            StepType = kind,
            Branch = branch,
            Iterations = kind == "loop" ? 0 : null,
        };
        _steps.Add(row);
        return row;
    }

    // Engine-owned variables ({last.*}, {loop.*}, {call.result}) change on every step; they are not the author's.
    private static List<PipelineTraceVariableChangeDto> Diff(
        Dictionary<string, string> before,
        Dictionary<string, string> after
    )
    {
        List<PipelineTraceVariableChangeDto> changes = [];
        foreach ((string key, string value) in after)
        {
            if (PipelineEngineVariables.All.Contains(key, StringComparer.OrdinalIgnoreCase))
                continue;
            if (!before.TryGetValue(key, out string? old) || old != value)
                changes.Add(new(key, old, value));
        }

        foreach ((string key, string value) in before)
        {
            if (PipelineEngineVariables.All.Contains(key, StringComparer.OrdinalIgnoreCase))
                continue;
            if (!after.ContainsKey(key))
                changes.Add(new(key, value, null));
        }

        return changes;
    }
}
