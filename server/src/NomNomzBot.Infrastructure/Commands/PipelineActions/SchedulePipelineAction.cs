// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Commands.PipelineActions;

/// <summary>
/// Schedules a saved pipeline to run ONCE after a delay — the generic building block behind timed follow-ups (a
/// voice-swap auto-revert, a feather auto-hide, a timed reward). Unlike <c>wait</c> (which holds the running
/// pipeline for the delay, occupying a concurrency slot), this persists a deferred task and returns immediately, so
/// the delay can be minutes/hours and survives a restart. Parameters: <c>pipeline</c> (the target pipeline's name,
/// resolved to an id in THIS tenant — a typed failure if unknown), <c>delay_seconds</c> (required; clamped to the
/// service's safe range), and optional <c>dedupe_key</c> (template-resolved; re-scheduling with the same key
/// replaces the pending run). The current context variables are captured so the deferred run keeps its context.
/// </summary>
public sealed class SchedulePipelineAction : ICommandAction
{
    private readonly IScheduledPipelineService _scheduler;
    private readonly ITemplateResolver _resolver;

    public string ActionType => "schedule_pipeline";
    public string Category => "flow";
    public string Description => "Schedule a saved pipeline to run once after a delay";

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
        if (string.IsNullOrWhiteSpace(pipelineName))
            return ActionResult.Failure("schedule_pipeline requires a 'pipeline' name");

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

        Result<ScheduledPipelineTaskDto> scheduled = await _scheduler.ScheduleByNameAsync(
            ctx.BroadcasterId,
            pipelineName,
            delaySeconds,
            captured,
            ctx.TriggeredByUserId,
            ctx.TriggeredByDisplayName,
            dedupeKey,
            ctx.CancellationToken
        );

        return scheduled.IsSuccess
            ? ActionResult.Success(
                $"Scheduled '{pipelineName}' to run in {delaySeconds}s (task {scheduled.Value.Id})"
            )
            : ActionResult.Failure(scheduled.ErrorMessage ?? "Failed to schedule pipeline");
    }
}
