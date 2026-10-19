// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
