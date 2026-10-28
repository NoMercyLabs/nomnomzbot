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

using NomNomzBot.Domain.Commands.ValueObjects;

namespace NomNomzBot.Application.Commands.Dtos;

/// <summary>
/// One deferred run — the projection the scheduler service returns and the pending-list surfaces.
/// <paramref name="Target"/> is what runs: a pipeline, or a command or action bound directly.
/// </summary>
public sealed record ScheduledPipelineTaskDto(
    Guid Id,
    Guid PipelineId,
    string? PipelineName,
    DateTimeOffset DueAt,
    string Status,
    string? DedupeKey,
    string TriggeredByDisplayName,
    DateTimeOffset CreatedAt,
    BindingTarget? Target = null
);
