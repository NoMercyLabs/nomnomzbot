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
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Enums;

namespace NomNomzBot.Infrastructure.Commands.PipelineActions;

/// <summary>
/// Schedules a saved pipeline to run ONCE after a delay — the generic building block behind timed follow-ups (a
/// voice-swap auto-revert, a feather auto-hide, a timed reward). Unlike <c>wait</c> (which holds the running
/// pipeline for the delay, occupying a concurrency slot), this persists a deferred task and returns immediately, so
/// the delay can be minutes/hours and survives a restart. Parameters: exactly one target — <c>pipeline</c> (the
/// pipeline's name), <c>command</c> (a command's name, run directly with no wrapper pipeline) or <c>action_type</c>
/// (an action run directly, with an optional <c>action_params</c> object) — each resolved or validated in THIS
/// tenant (a typed failure if unknown or foreign), <c>delay_seconds</c> (required; clamped to the
/// service's safe range), and optional <c>dedupe_key</c> (template-resolved; re-scheduling with the same key
/// replaces the pending run). The current context variables are captured so the deferred run keeps its context.
/// </summary>
public sealed class SchedulePipelineAction : ICommandAction
{
    private readonly IScheduledPipelineService _scheduler;
    private readonly ITemplateResolver _resolver;

    public string ActionType => "schedule_pipeline";

    public LocalizedText Category => new("pipeline.category.flow");

    public LocalizedText Description => new("pipeline.schedule_pipeline.description");

    public bool ResolvesOwnTemplates => true;

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "pipeline",
                PipelineActionFieldKind.Text,
                Description: new("pipeline.schedule_pipeline.pipeline.help")
            ),
            new(
                "command",
                PipelineActionFieldKind.Text,
                Description: new("pipeline.schedule_pipeline.command.help")
            ),
            new(
                "action_type",
                PipelineActionFieldKind.Text,
                Description: new("pipeline.schedule_pipeline.action_type.help")
            ),
            new(
                "delay_seconds",
                PipelineActionFieldKind.Number,
                Required: true,
                Description: new("pipeline.schedule_pipeline.delay_seconds.help")
            ),
            new(
                "dedupe_key",
                PipelineActionFieldKind.Text,
                Templated: true,
                Description: new("pipeline.schedule_pipeline.dedupe_key.help")
            ),
        ];

    public SchedulePipelineAction(IScheduledPipelineService scheduler, ITemplateResolver resolver)
    {
        _scheduler = scheduler;
        _resolver = resolver;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string? pipelineName = action.GetString("pipeline");
        string? commandName = action.GetString("command");
        string? actionType = action.GetString("action_type");
        int targets = new[] { pipelineName, commandName, actionType }.Count(n =>
            !string.IsNullOrWhiteSpace(n)
        );
        if (targets != 1)
            return ActionResult.Failure(
                "schedule_pipeline needs exactly one of 'pipeline', 'command' or 'action_type'"
            );

        int delaySeconds = action.GetInt("delay_seconds", 0);
        if (delaySeconds <= 0)
            return ActionResult.Failure("schedule_pipeline requires a positive 'delay_seconds'");

        // Optional dedupe key is a template so it can name the deferred run per-user/per-target
        // (e.g. "voice-swap-revert:{user.id}") — the same key later replaces the still-pending run.
        string? dedupeTemplate = action.GetString("dedupe_key");
        string? dedupeKey = string.IsNullOrWhiteSpace(dedupeTemplate)
            ? null
            : await _resolver.ResolveAsync(
                dedupeTemplate,
                ctx.Variables,
                ctx.BroadcasterId,
                ctx.CancellationToken
            );

        // Snapshot the current variables so the deferred run keeps the context it was scheduled from.
        Dictionary<string, string> captured = new(ctx.Variables, StringComparer.OrdinalIgnoreCase);

        string targetName = pipelineName ?? commandName ?? actionType!;
        Result<ScheduledPipelineTaskDto> scheduled = await ScheduleAsync(
            ctx,
            action,
            (pipelineName, commandName, actionType),
            delaySeconds,
            captured,
            dedupeKey
        );

        return scheduled.IsSuccess
            ? ActionResult.Success(
                $"Scheduled '{targetName}' to run in {delaySeconds}s (task {scheduled.Value.Id})"
            )
            : ActionResult.Failure(scheduled.ErrorMessage ?? "Failed to schedule pipeline");
    }

    private Task<Result<ScheduledPipelineTaskDto>> ScheduleAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action,
        (string? Pipeline, string? Command, string? ActionType) names,
        int delaySeconds,
        IReadOnlyDictionary<string, string> variables,
        string? dedupeKey
    )
    {
        if (!string.IsNullOrWhiteSpace(names.Command))
            return _scheduler.ScheduleCommandByNameAsync(
                ctx.BroadcasterId,
                names.Command,
                delaySeconds,
                variables,
                ctx.TriggeredByUserId,
                ctx.TriggeredByDisplayName,
                dedupeKey,
                ctx.CancellationToken
            );

        if (!string.IsNullOrWhiteSpace(names.ActionType))
            return _scheduler.ScheduleTargetAsync(
                ctx.BroadcasterId,
                new(
                    BindingTargetKind.Action,
                    ActionType: names.ActionType,
                    Params: ReadActionParams(action)
                ),
                delaySeconds,
                variables,
                ctx.TriggeredByUserId,
                ctx.TriggeredByDisplayName,
                dedupeKey,
                ctx.CancellationToken
            );

        return _scheduler.ScheduleByNameAsync(
            ctx.BroadcasterId,
            names.Pipeline!,
            delaySeconds,
            variables,
            ctx.TriggeredByUserId,
            ctx.TriggeredByDisplayName,
            dedupeKey,
            ctx.CancellationToken
        );
    }

    private static Dictionary<string, JsonElement>? ReadActionParams(ActionDefinition action) =>
        action.Parameters is not null
        && action.Parameters.TryGetValue("action_params", out JsonElement raw)
        && raw.ValueKind == JsonValueKind.Object
            ? raw.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())
            : null;
}
