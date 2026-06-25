// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Pipeline;

public interface IPipelineEngine
{
    Task<PipelineExecutionResult> ExecuteAsync(
        PipelineRequest request,
        CancellationToken ct = default
    );
    Task CancelAllForChannelAsync(Guid broadcasterId);
    int GetActiveCountForChannel(Guid broadcasterId);
}

public class PipelineRequest
{
    /// <summary>The tenant (channel) Guid this pipeline runs for (schema §1.1, internal key).</summary>
    public required Guid BroadcasterId { get; init; }

    /// <summary>
    /// When set, the engine loads PipelineStep rows from the database (preferred path).
    /// Falls back to <see cref="PipelineJson"/> if no steps are found.
    /// </summary>
    public Guid? PipelineId { get; init; }

    /// <summary>
    /// Legacy / fallback graph JSON. Used when PipelineId is null or has no DB steps.
    /// </summary>
    public string PipelineJson { get; init; } = "{}";

    public required string TriggeredByUserId { get; init; }
    public required string TriggeredByDisplayName { get; init; }
    public string? MessageId { get; init; }
    public string? RedemptionId { get; init; }
    public string? RewardId { get; init; }
    public string RawMessage { get; init; } = "";
    public Dictionary<string, string> InitialVariables { get; init; } = new();
}

public class PipelineExecutionResult
{
    public required string ExecutionId { get; init; }
    public required PipelineOutcome Outcome { get; init; }
    public required TimeSpan Duration { get; init; }
    public int StepsExecuted { get; init; }
    public int StepsSkipped { get; init; }
    public int Total { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<StepExecutionLog> StepLogs { get; init; } = [];
}

public enum PipelineOutcome
{
    Completed,
    Stopped,
    Failed,
    TimedOut,
    Cancelled,
}

public class StepExecutionLog
{
    public required int StepIndex { get; init; }
    public required string ActionType { get; init; }
    public required bool Succeeded { get; init; }
    public required TimeSpan Duration { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Output { get; init; }
}
