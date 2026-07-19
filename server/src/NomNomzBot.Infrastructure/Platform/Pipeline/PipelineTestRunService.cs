// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.TestRun;

namespace NomNomzBot.Infrastructure.Platform.Pipeline;

/// <summary>
/// The pipeline DRY-RUN (commands-pipelines.md). Runs the saved pipeline through a scoped, REAL
/// <see cref="PipelineEngine"/> — but with every side-effecting action swapped for a <see cref="CapturingCommandAction"/>
/// keyed by action type, while read-only, branching-relevant actions (variable math, stop, pick-list draws, balance
/// reads) and ALL conditions run for real. So conditions are evaluated against live data and captured actions report
/// success (<c>{last.success}=true</c>), exercising downstream branches as the happy path without touching chat, TTS,
/// overlays, moderation, the economy, rewards, or a nested script. Nothing is persisted.
/// </summary>
public sealed class PipelineTestRunService(
    IApplicationDbContext db,
    ICurrentTenantService tenant,
    IEnumerable<ICommandAction> actions,
    IEnumerable<ICommandCondition> conditions,
    IChannelRegistry registry,
    ITemplateResolver resolver,
    ILogger<PipelineEngine> logger,
    TimeProvider timeProvider
) : IPipelineTestRunService
{
    // The only actions run for real: side-effect-free, and each contributes to realistic branching. Everything else
    // is captured (default-deny) so a newly added side-effecting action can never accidentally fire in a dry-run.
    private static readonly HashSet<string> PassthroughActionTypes = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "set_variable",
        "stop",
        "pick_from_list",
        "check_balance",
    };

    public async Task<Result<TestRunResultDto>> RunAsync(
        Guid pipelineId,
        PipelineTestRunRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (tenant.BroadcasterId is not Guid broadcasterId)
            return Result.Failure<TestRunResultDto>("No tenant.", "NO_TENANT");

        bool exists = await db.Pipelines.AnyAsync(
            p => p.Id == pipelineId && p.BroadcasterId == broadcasterId,
            cancellationToken
        );
        if (!exists)
            return Result.Failure<TestRunResultDto>("Pipeline not found.", "NOT_FOUND");

        CaptureSink sink = new();
        List<ICommandAction> captureActions =
        [
            .. actions.Select(a =>
                PassthroughActionTypes.Contains(a.ActionType)
                    ? a
                    : (ICommandAction)new CapturingCommandAction(a, sink, resolver)
            ),
        ];

        // A fresh engine over the SAME real dependencies but the capture action-set — the real engine logic
        // (step order, conditions, {last.*} wiring, concurrency/timeout) is exercised unchanged.
        PipelineEngine engine = new(db, registry, captureActions, conditions, logger, timeProvider);

        PipelineRequest pipelineRequest = new()
        {
            BroadcasterId = broadcasterId,
            PipelineId = pipelineId,
            TriggeredByUserId = broadcasterId.ToString(),
            TriggeredByDisplayName = "Test Run",
            MessageId = null,
            RawMessage = string.Empty,
            InitialVariables = new Dictionary<string, string>(
                request.Variables,
                StringComparer.OrdinalIgnoreCase
            ),
        };

        PipelineExecutionResult result = await engine.ExecuteAsync(
            pipelineRequest,
            cancellationToken
        );

        bool anyStepFailed = result.StepLogs.Any(l => !l.Succeeded);
        bool success = result.Outcome == PipelineOutcome.Completed && !anyStepFailed;

        string? error = result.ErrorMessage;
        if (error is null && anyStepFailed)
            error = result.StepLogs.First(l => !l.Succeeded).ErrorMessage;

        List<string> log =
        [
            .. result.StepLogs.Select(l =>
            {
                string status = l.Succeeded ? "ok" : "FAILED";
                string detail =
                    l.ErrorMessage is not null ? $" error: {l.ErrorMessage}"
                    : l.Output is not null ? $": {l.Output}"
                    : string.Empty;
                return $"[{l.StepIndex}] {l.ActionType} — {status}{detail}";
            }),
        ];

        return Result.Success(
            new TestRunResultDto(
                success,
                success ? null : error,
                (long)result.Duration.TotalMilliseconds,
                sink.Effects.Count,
                sink.Effects,
                sink.ChatOutput,
                log
            )
        );
    }
}
