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
