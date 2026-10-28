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

using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.ValueObjects;

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// Schedules DEFERRED, one-shot pipeline runs — "run pipeline P once, T seconds from now, with these variables" —
/// that survive a process restart (persisted as <c>ScheduledPipelineTask</c> rows; a background sweeper fires them
/// when due). The generic primitive behind timed follow-ups (a voice-swap auto-revert, a feather auto-hide, a
/// timed reward). Distinct from the <c>wait</c> action (which occupies a live pipeline slot), a recurring
/// <c>Timer</c>, and a reward-scoped <c>RedemptionTimer</c>.
/// </summary>
public interface IScheduledPipelineService
{
    /// <summary>
    /// Schedules the pipeline <paramref name="pipelineId"/> to run once after <paramref name="delaySeconds"/>
    /// (clamped to a sane range: min 1s, max 24h). <paramref name="variables"/> are carried through to the deferred
    /// run as its initial variables. When <paramref name="dedupeKey"/> is set, an existing PENDING task with the
    /// same key is REPLACED (its due time + variables updated) rather than a second one stacked.
    /// </summary>
    Task<Result<ScheduledPipelineTaskDto>> ScheduleAsync(
        Guid broadcasterId,
        Guid pipelineId,
        int delaySeconds,
        IReadOnlyDictionary<string, string> variables,
        string triggeredByUserId,
        string triggeredByDisplayName,
        string? dedupeKey = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// As <see cref="ScheduleAsync"/>, but resolves <paramref name="pipelineName"/> to a pipeline id within the
    /// tenant first (case-insensitive, enabled pipelines only). Fails NOT_FOUND when no such pipeline exists — the
    /// name-first entry point the <c>schedule_pipeline</c> action and the <c>schedule.pipeline</c> script capability
    /// share.
    /// </summary>
    Task<Result<ScheduledPipelineTaskDto>> ScheduleByNameAsync(
        Guid broadcasterId,
        string pipelineName,
        int delaySeconds,
        IReadOnlyDictionary<string, string> variables,
        string triggeredByUserId,
        string triggeredByDisplayName,
        string? dedupeKey = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// As <see cref="ScheduleAsync"/>, but the deferred run is any binding target: a command or an action runs
    /// directly through the command invoker (no wrapper pipeline), a pipeline target behaves as
    /// <see cref="ScheduleAsync"/>. Fails VALIDATION_FAILED when the target is not valid in this channel.
    /// </summary>
    Task<Result<ScheduledPipelineTaskDto>> ScheduleTargetAsync(
        Guid broadcasterId,
        BindingTarget target,
        int delaySeconds,
        IReadOnlyDictionary<string, string> variables,
        string triggeredByUserId,
        string triggeredByDisplayName,
        string? dedupeKey = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// As <see cref="ScheduleTargetAsync"/> for a command named <paramref name="commandName"/> (a leading
    /// <c>!</c> and letter case are ignored): resolves the name to a command id within the tenant first. Fails
    /// NOT_FOUND when the channel has no such command — the name-first entry point the <c>schedule_pipeline</c>
    /// action uses for its <c>command</c> parameter.
    /// </summary>
    Task<Result<ScheduledPipelineTaskDto>> ScheduleCommandByNameAsync(
        Guid broadcasterId,
        string commandName,
        int delaySeconds,
        IReadOnlyDictionary<string, string> variables,
        string triggeredByUserId,
        string triggeredByDisplayName,
        string? dedupeKey = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Cancels a specific pending task (marks it cancelled). No-op success if already terminal.</summary>
    Task<Result> CancelAsync(
        Guid broadcasterId,
        Guid taskId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Cancels every pending task in the channel carrying <paramref name="dedupeKey"/>.</summary>
    Task<Result> CancelByDedupeKeyAsync(
        Guid broadcasterId,
        string dedupeKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Lists the channel's still-pending scheduled tasks, soonest-due first.</summary>
    Task<IReadOnlyList<ScheduledPipelineTaskDto>> ListPendingAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Fires every due pending task across all tenants (the background-sweeper entry point): marks each terminal
    /// FIRST (so a crash mid-dispatch can never re-fire), then dispatches it through the pipeline engine. A task
    /// overdue beyond the stale-grace window is EXPIRED instead of dispatched (a long-late deferred action is
    /// wrong to run). Returns how many rows transitioned terminal.
    /// </summary>
    Task<int> FireDueAsync(CancellationToken cancellationToken = default);
}
