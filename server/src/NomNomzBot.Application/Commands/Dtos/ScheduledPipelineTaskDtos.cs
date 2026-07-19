// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Commands.Dtos;

/// <summary>One deferred pipeline run — the projection the scheduler service returns and the pending-list surfaces.</summary>
public sealed record ScheduledPipelineTaskDto(
    Guid Id,
    Guid PipelineId,
    string? PipelineName,
    DateTimeOffset DueAt,
    string Status,
    string? DedupeKey,
    string TriggeredByDisplayName,
    DateTimeOffset CreatedAt
);
