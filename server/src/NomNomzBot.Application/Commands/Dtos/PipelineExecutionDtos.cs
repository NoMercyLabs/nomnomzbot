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

using NomNomzBot.Domain.Commands.Enums;

namespace NomNomzBot.Application.Commands.Dtos;

/// <summary>Lightweight run summary for the execution-history list view (H.4 PipelineExecution).
/// <c>FailureKind</c> is null on a run that did not fail; <c>LimitKey</c> and <c>LimitValue</c> are set only
/// when a limit or a time budget stopped the run.</summary>
public sealed record PipelineExecutionSummaryDto(
    long Id,
    Guid? PipelineId,
    Guid? TriggeredByUserId,
    string TriggerKind,
    string Status,
    int HostCallCount,
    int DurationMs,
    string? ErrorMessage,
    RunFailureKind? FailureKind,
    string? LimitKey,
    long? LimitValue,
    DateTime StartedAt,
    DateTime? CompletedAt
);

/// <summary>One host call a step made (for example <c>tts.speak</c>), with how long it took.</summary>
public sealed record PipelineExecutionHostCallDto(
    string Name,
    int DurationMs,
    bool Ok,
    string? Error
);

/// <summary>One step's outcome within a run, so the FAILING step is identifiable in the detail read.</summary>
public sealed record PipelineExecutionStepLogDto(
    int StepIndex,
    string ActionType,
    bool Succeeded,
    int DurationMs,
    string? ErrorMessage,
    IReadOnlyList<PipelineExecutionHostCallDto> HostCalls
);

/// <summary>Full run detail — the summary fields plus its ordered per-step logs.</summary>
public sealed record PipelineExecutionDetailDto(
    long Id,
    Guid? PipelineId,
    Guid? TriggeredByUserId,
    string TriggerKind,
    string Status,
    int HostCallCount,
    int DurationMs,
    string? ErrorMessage,
    RunFailureKind? FailureKind,
    string? LimitKey,
    long? LimitValue,
    DateTime StartedAt,
    DateTime? CompletedAt,
    IReadOnlyList<PipelineExecutionStepLogDto> StepLogs
);

/// <summary>How busy and how healthy a channel's runs are since a moment: run and failure counts, and the
/// newest failed run of all time (so a quiet day still shows the last thing that broke).</summary>
public sealed record PipelineExecutionActivityDto(
    int Runs,
    int FailedRuns,
    long? LastFailedRunId,
    DateTime? LastFailedRunAt,
    string? LastFailedRunError
);
