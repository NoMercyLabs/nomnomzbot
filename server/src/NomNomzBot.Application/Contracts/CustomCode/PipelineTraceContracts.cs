// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.CustomCode;

/// <summary>One variable a traced step changed: the value before the step ran and the value after (null = unset).</summary>
public sealed record PipelineTraceVariableChangeDto(string Key, string? Before, string? After);

/// <summary>
/// One row of a pipeline test-run trace: what one executed step did. <c>Branch</c> names the arm an <c>if</c> took
/// (<c>then</c>/<c>else</c>); <c>Iterations</c> is the pass count of a <c>loop</c>; <c>VariableChanges</c> lists only the
/// author's variables whose value changed; <c>Output</c> is what the step produced (e.g. the chat text it would send);
/// <c>Error</c> is set when the step failed. Rows are in the order the steps ran.
/// </summary>
public sealed class PipelineTraceStepDto
{
    public required string StepId { get; init; }
    public required string StepType { get; init; }
    public string? Branch { get; init; }
    public int? Iterations { get; set; }
    public IReadOnlyList<PipelineTraceVariableChangeDto> VariableChanges { get; init; } = [];
    public string? Output { get; init; }
    public string? Error { get; init; }
}
