// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// Read-only access to the append-only H.4 <c>PipelineExecution</c> telemetry (S008b, 75519b88) — the
/// history a streamer needs to see why a command/event-response misbehaved.
/// </summary>
public interface IPipelineExecutionQueryService
{
    /// <summary>Lists the channel's runs, newest-first, real-paginated. <paramref name="failuresOnly"/>
    /// restricts to non-success outcomes (failed / partially_failed / timed_out / cancelled).</summary>
    Task<Result<PagedList<PipelineExecutionSummaryDto>>> ListAsync(
        string broadcasterId,
        PaginationParams pagination,
        bool failuresOnly,
        CancellationToken ct = default
    );

    /// <summary>One run's detail, including its ordered per-step logs.</summary>
    Task<Result<PipelineExecutionDetailDto>> GetDetailAsync(
        string broadcasterId,
        long id,
        CancellationToken ct = default
    );
}
