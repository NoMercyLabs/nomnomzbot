// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;

namespace NomNomzBot.Application.Commands.Services;

/// <summary>Seed variables for a pipeline dry-run (test-run) — no PII, author-supplied.</summary>
public sealed record PipelineTestRunRequest(IReadOnlyDictionary<string, string> Variables);

/// <summary>
/// Executes a saved pipeline through the real engine in CAPTURE mode (commands-pipelines.md): reads, conditions,
/// pick-list draws and variable math run for real, but every side-effecting action (chat/tts/widget/moderation/
/// economy-write/reward/schedule/run_code/…) is RECORDED rather than performed. A captured action reports success so
/// downstream branches are exercised as the happy path. Never touches an external surface or persists anything.
/// </summary>
public interface IPipelineTestRunService
{
    Task<Result<TestRunResultDto>> RunAsync(
        Guid pipelineId,
        PipelineTestRunRequest request,
        CancellationToken cancellationToken = default
    );
}
