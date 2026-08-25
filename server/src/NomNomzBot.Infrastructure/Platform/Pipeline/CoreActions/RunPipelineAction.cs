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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;

/// <summary>
/// Invokes another of the channel's pipelines (pipeline-control-flow.md D4). Two modes:
///
/// - <c>inline</c> (default) — runs within the CALLER's own execution: shares its Run-scope
///   variables and its recursion-depth counter (<see cref="PipelineExecutionContext.CallDepth"/>),
///   and — if the callee reaches a <c>return_value</c> step — binds the result into
///   <c>{{call.result}}</c> for the caller's next step. A failing/aborted callee makes THIS step
///   fail, exactly like any other action — an enclosing <c>try</c> at the call site catches it the
///   same way it catches every other failing leaf.
/// - <c>detached</c> — dispatches an entirely independent run via <see cref="IPipelineEngine.ExecuteAsync"/>
///   (fresh context, own <c>PipelineExecution</c> row, own concurrency gate); never shares variables
///   back and never populates <c>{{call.result}}</c> (pipeline-tree-and-editor.md §2.2). <c>wait</c>
///   (default <c>true</c>) blocks for the detached run's outcome; <c>false</c> fires it and returns
///   immediately without waiting.
///
/// Both modes fail closed when the target pipeline id does not resolve to a pipeline owned by the
/// CALLER's own channel — a pipeline can never reach into another channel's automation
/// (platform-conventions.md tenant isolation).
/// </summary>
public sealed class RunPipelineAction : ICommandAction
{
    // Resolved lazily per call, never captured — PipelineEngine itself is constructed from
    // IEnumerable<ICommandAction> (this action included), so taking IPipelineEngine directly in the
    // constructor would be a circular DI dependency. IServiceProvider has no such cycle; the same
    // scope this action was resolved in hands back the very same scoped IPipelineEngine instance.
    private readonly IServiceProvider _serviceProvider;
    private readonly IApplicationDbContext _db;

    public RunPipelineAction(IServiceProvider serviceProvider, IApplicationDbContext db)
    {
        _serviceProvider = serviceProvider;
        _db = db;
    }

    public string ActionType => "run_pipeline";

    public string Category => "flow";

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new("pipeline", PipelineActionFieldKind.ResourceId, Required: true),
            new(
                "mode",
                PipelineActionFieldKind.Enum,
                Required: false,
                Options: ["inline", "detached"]
            ),
            new("args", PipelineActionFieldKind.Text, Required: false),
            new("wait", PipelineActionFieldKind.Boolean, Required: false),
        ];

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string? pipelineIdRaw = action.GetString("pipeline");
        if (!Guid.TryParse(pipelineIdRaw, out Guid targetPipelineId))
            return ActionResult.Failure("run_pipeline requires a valid 'pipeline' id");

        string mode = action.GetString("mode") ?? "inline";
        List<string>? args = GetArgsList(action);
        IPipelineEngine engine = _serviceProvider.GetRequiredService<IPipelineEngine>();

        if (string.Equals(mode, "detached", StringComparison.OrdinalIgnoreCase))
            return await ExecuteDetachedAsync(engine, ctx, targetPipelineId, args, action);

        Result<string?> inlineResult = await engine.RunInlineSubPipelineAsync(
            ctx,
            targetPipelineId,
            args,
            ctx.CancellationToken
        );

        if (inlineResult.IsFailure)
            return ActionResult.Failure(
                inlineResult.ErrorMessage ?? "run_pipeline inline call failed"
            );

        ctx.Variables["call.result"] = inlineResult.Value ?? string.Empty;
        return ActionResult.Success(inlineResult.Value);
    }

    private async Task<ActionResult> ExecuteDetachedAsync(
        IPipelineEngine engine,
        PipelineExecutionContext ctx,
        Guid targetPipelineId,
        List<string>? args,
        ActionDefinition action
    )
    {
        // Fail closed up front — never spawn a detached run against a pipeline this channel does
        // not own, whether or not the caller waits for it.
        bool owned = await _db
            .Pipelines.AsNoTracking()
            .AnyAsync(
                p => p.Id == targetPipelineId && p.BroadcasterId == ctx.BroadcasterId,
                ctx.CancellationToken
            );
        if (!owned)
            return ActionResult.Failure("run_pipeline: target pipeline not found in this channel");

        Dictionary<string, string> initialVariables = [];
        if (args is not null)
            for (int i = 0; i < args.Count; i++)
                initialVariables[$"args.{i + 1}"] = args[i];

        PipelineRequest request = new()
        {
            BroadcasterId = ctx.BroadcasterId,
            PipelineId = targetPipelineId,
            TriggeredByUserId = ctx.TriggeredByUserId,
            TriggeredByDisplayName = ctx.TriggeredByDisplayName,
            MessageId = ctx.MessageId,
            RawMessage = ctx.RawMessage,
            InitialVariables = initialVariables,
        };

        bool wait = action.GetBool("wait", defaultValue: true);
        if (!wait)
        {
            _ = engine.ExecuteAsync(request, CancellationToken.None);
            return ActionResult.Success("detached run dispatched");
        }

        PipelineExecutionResult detachedResult = await engine.ExecuteAsync(
            request,
            ctx.CancellationToken
        );
        bool succeeded =
            detachedResult.Outcome is PipelineOutcome.Completed or PipelineOutcome.Stopped;
        return succeeded
            ? ActionResult.Success($"detached run {detachedResult.Outcome}")
            : ActionResult.Failure($"detached run {detachedResult.Outcome}");
    }

    /// <summary>Positional args only (pipeline-tree-and-editor.md §2.5's named-parameter binding needs
    /// a new <c>Pipeline.ParameterNamesJson</c> column — out of this slice's no-migration scope; a
    /// callee always reads its args as <c>{{args.1}}</c>..<c>{{args.N}}</c>, same as a chat-command
    /// invocation today).</summary>
    private static List<string>? GetArgsList(ActionDefinition action)
    {
        if (
            action.Parameters is null
            || !action.Parameters.TryGetValue("args", out JsonElement elem)
        )
            return null;

        if (elem.ValueKind != JsonValueKind.Array)
            return null;

        List<string> result = [];
        foreach (JsonElement item in elem.EnumerateArray())
            result.Add(
                item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.ToString()
            );
        return result;
    }
}
