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

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>
/// One due scheduled task, already marked fired, handed to the <see cref="IScheduledPipelineDispatcher"/>. A plain
/// snapshot (never the tracked entity): the run outlives the sweep's DbContext.
/// </summary>
public sealed record ScheduledPipelineRun(
    Guid TaskId,
    string? PipelineName,
    PipelineRequest Request
);
