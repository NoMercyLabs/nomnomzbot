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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Widgets.Services;
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
    TimeProvider timeProvider,
    IPipelineService pipelines,
    ICurrentUserService currentUser,
    IOverlayPresenceRegistry overlays
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
        if (tenant.BroadcasterId is not { } broadcasterId)
            return Result.Failure<TestRunResultDto>("No tenant.", "NO_TENANT");

        string mode = request.Mode?.Trim().ToLowerInvariant() ?? PipelineTestRunModes.Dry;
        if (mode is not (PipelineTestRunModes.Dry or PipelineTestRunModes.Live))
            return Result.Failure<TestRunResultDto>(
                $"Unknown test run mode '{request.Mode}'. Use 'dry' or 'live'.",
                "VALIDATION_FAILED"
            );
        bool live = mode == PipelineTestRunModes.Live;
        if (live && string.IsNullOrWhiteSpace(currentUser.UserId))
            return Result.Failure<TestRunResultDto>(
                "A live run needs a signed-in caller.",
                "UNAUTHENTICATED"
            );

        // Load the graph document, exactly like the real dispatch (reward/command/timer all read
        // GraphJsonCache into PipelineRequest.PipelineJson). Pipelines are stored graph-only — there are no
        // PipelineStep rows — so a test-run that passed only the id ran zero steps. Wrap the projection so a
        // missing row (null wrapper → NOT_FOUND) is distinct from a found pipeline with an empty graph.
        var found = await db
            .Pipelines.Where(p => p.Id == pipelineId && p.BroadcasterId == broadcasterId)
            .Select(p => new { p.GraphJsonCache, p.ParameterNamesJson })
            .FirstOrDefaultAsync(cancellationToken);
        if (found is null)
            return Result.Failure<TestRunResultDto>("Pipeline not found.", "NOT_FOUND");
        if (request.InputDefinitions is not null && request.Graph is null)
            return Result.Failure<TestRunResultDto>(
                "Draft input declarations require a draft graph.",
                "VALIDATION_FAILED"
            );
        Result<Dictionary<string, string>> boundInputs = PipelineInputs.Bind(
            request.InputDefinitions ?? PipelineInputs.Parse(found.ParameterNamesJson),
            request.Inputs
        );
        if (boundInputs.IsFailure)
            return Result.Failure<TestRunResultDto>(
                boundInputs.ErrorMessage!,
                boundInputs.ErrorCode!
            );
        string graphJson = string.IsNullOrWhiteSpace(found.GraphJsonCache)
            ? "{}"
            : found.GraphJsonCache;

        Guid? runPipelineId = pipelineId;
        if (request.Graph is { } draft)
        {
            // The draft a person is editing: held to the same gate as a save, then run in place of the saved
            // graph. PipelineId null makes the engine read this graph (and build tree rows in memory).
            Result<string?> validated = await pipelines.ValidateGraphAsync(
                draft,
                cancellationToken
            );
            if (validated.IsFailure)
                return Result.Failure<TestRunResultDto>(
                    validated.ErrorMessage,
                    validated.ErrorCode
                );
            graphJson = validated.Value ?? "{}";
            runPipelineId = null;
        }

        if (request.Step is { } step)
        {
            if (
                step.ValueKind != JsonValueKind.Object
                || !step.TryGetProperty("type", out JsonElement type)
                || type.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(type.GetString())
            )
                return Result.Failure<TestRunResultDto>(
                    "A step needs an action type.",
                    "INVALID_STEP"
                );
            // One step, no step rows: PipelineId null makes the engine read this graph instead.
            graphJson = $$"""{"steps":[{"action":{{step.GetRawText()}}}]}""";
            runPipelineId = null;
        }

        // A live run does real things: one at a time per pipeline, so a double click never fires it twice.
        using IDisposable? liveLease = live ? LivePipelineRunGate.TryEnter(pipelineId) : null;
        if (live && liveLease is null)
            return Result.Failure<TestRunResultDto>(
                "A live run of this pipeline is already running. Wait for it to finish.",
                "LIVE_RUN_IN_PROGRESS"
            );

        CaptureSink sink = new();
        // Dry: every side-effecting action is captured. Live: the real action set runs, unchanged.
        List<ICommandAction> runActions = live
            ? [.. actions]
            :
            [
                .. actions.Select(a =>
                    PassthroughActionTypes.Contains(a.ActionType)
                        ? a
                        : new CapturingCommandAction(a, sink, resolver)
                ),
            ];

        // A fresh engine over the SAME real dependencies but the chosen action-set — the real engine logic
        // (step order, conditions, {last.*} wiring, concurrency/timeout) is exercised unchanged.
        PipelineEngine engine = new(
            db,
            registry,
            runActions,
            conditions,
            resolver,
            logger,
            timeProvider
        );

        TestRunTrigger trigger = live
            ? TestRunTrigger.Live(request.Variables, currentUser)
            : TestRunTrigger.Dry(request.Variables, broadcasterId);
        foreach ((string name, string value) in boundInputs.Value)
            trigger.Variables[name] = value;

        // Pass BOTH the id and the graph: the engine uses PipelineStep rows when they exist and falls back
        // to PipelineJson otherwise (its own resolution order). Real pipelines are graph-only, so the graph
        // is what actually runs — but this also honors any step-row pipeline without special-casing.
        PipelineRequest pipelineRequest = new()
        {
            BroadcasterId = broadcasterId,
            PipelineId = runPipelineId,
            PipelineJson = graphJson,
            TriggeredByUserId = trigger.UserId,
            TriggeredByDisplayName = trigger.DisplayName,
            MessageId = null,
            RawMessage = string.Empty,
            InitialVariables = trigger.Variables,
            CollectTrace = true,
            RunInlineGraphAsTree = request.Graph is not null,
            TestRunOfPipelineId = pipelineId,
            LiveTestRun = live,
        };

        PipelineExecutionResult result = await engine.ExecuteAsync(
            pipelineRequest,
            cancellationToken
        );

        bool anyStepFailed = result.StepLogs.Any(l => !l.Succeeded);
        // Stopped is a deliberate `stop`/`stop_on_match` step — the run still did its intended work.
        // PartiallyFailed (a failed action broke the run early) must never count as success.
        bool success =
            result.Outcome is PipelineOutcome.Completed or PipelineOutcome.Stopped
            && !anyStepFailed;

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
                log,
                VariablesSet: null,
                Console: null
            )
            {
                Trace = result.Trace,
                Mode = mode,
                RealSteps = live ? [.. result.StepLogs.Select(l => l.ActionType)] : [],
                Warnings = live
                    ? LiveRunOverlayWarnings.For(
                        result.StepLogs,
                        overlays.IsOverlayConnected(broadcasterId)
                    )
                    : [],
            }
        );
    }
}
