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

namespace NomNomzBot.Application.Commands.Dtos;

/// <summary>Lightweight run summary for the execution-history list view (H.4 PipelineExecution).</summary>
public sealed record PipelineExecutionSummaryDto(
    long Id,
    Guid? PipelineId,
    string TriggerKind,
    string Status,
    int HostCallCount,
    int DurationMs,
    string? ErrorMessage,
    DateTime StartedAt,
    DateTime? CompletedAt
);

/// <summary>One step's outcome within a run, so the FAILING step is identifiable in the detail read.</summary>
public sealed record PipelineExecutionStepLogDto(
    int StepIndex,
    string ActionType,
    bool Succeeded,
    int DurationMs,
    string? ErrorMessage
);

/// <summary>Full run detail — the summary fields plus its ordered per-step logs.</summary>
public sealed record PipelineExecutionDetailDto(
    long Id,
    Guid? PipelineId,
    string TriggerKind,
    string Status,
    int HostCallCount,
    int DurationMs,
    string? ErrorMessage,
    DateTime StartedAt,
    DateTime? CompletedAt,
    IReadOnlyList<PipelineExecutionStepLogDto> StepLogs
);
