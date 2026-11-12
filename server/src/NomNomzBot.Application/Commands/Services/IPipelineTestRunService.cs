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

using System.Text.Json;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// Seed variables for a pipeline dry-run (test-run) — no PII, author-supplied. <paramref name="Step"/> is optional:
/// one action object (<c>{"type":"send_message", ...params}</c>); when set, only that action runs, through the
/// same capture engine, instead of the saved pipeline's graph. <paramref name="Graph"/> is optional too: the draft
/// graph a person is editing (the same JSON the save takes); when set it is validated like a save and runs
/// instead of the saved graph, flat or tree. <paramref name="Mode"/> is <see cref="PipelineTestRunModes.Dry"/> when
/// absent (the capture run, unchanged) or <see cref="PipelineTestRunModes.Live"/> (the real actions run, with the
/// variables as the start values; the trigger user defaults to the caller). <paramref name="InputDefinitions"/>
/// supplies unsaved declarations alongside a draft <paramref name="Graph"/>.
/// </summary>
public sealed record PipelineTestRunRequest(
    IReadOnlyDictionary<string, string> Variables,
    JsonElement? Step = null,
    JsonElement? Graph = null,
    string? Mode = null,
    IReadOnlyDictionary<string, JsonElement>? Inputs = null,
    IReadOnlyList<PipelineInputDefinition>? InputDefinitions = null
);

/// <summary>The two ways a pipeline test run can go. The wire value of <see cref="PipelineTestRunRequest.Mode"/>.</summary>
public static class PipelineTestRunModes
{
    /// <summary>Every side-effecting action is captured, nothing outward happens. The default.</summary>
    public const string Dry = "dry";

    /// <summary>The real actions run for real, as if the trigger had fired.</summary>
    public const string Live = "live";
}

/// <summary>
/// Executes a saved pipeline through the real engine in CAPTURE mode (commands-pipelines.md): reads, conditions,
/// pick-list draws and variable math run for real, but every side-effecting action (chat/tts/widget/moderation/
/// economy-write/reward/schedule/run_code/…) is RECORDED rather than performed. A captured action reports success so
/// downstream branches are exercised as the happy path. A dry run never touches an external surface. A
/// <see cref="PipelineTestRunModes.Live"/> run is the opposite: the real actions run, and the run is filed in the
/// pipeline's run history as <c>manual_live</c>.
/// </summary>
public interface IPipelineTestRunService
{
    Task<Result<TestRunResultDto>> RunAsync(
        Guid pipelineId,
        PipelineTestRunRequest request,
        CancellationToken cancellationToken = default
    );
}
