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

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// Read-only access to the append-only H.4 <c>PipelineExecution</c> telemetry (S008b, 75519b88) — the
/// history a streamer needs to see why a command/event-response misbehaved.
/// </summary>
public interface IPipelineExecutionQueryService
{
    /// <summary>Lists the channel's runs, newest-first, real-paginated. <paramref name="failuresOnly"/>
    /// restricts to non-success outcomes (failed / partially_failed / timed_out / cancelled).
    /// <paramref name="pipelineId"/>, when set, restricts to the runs of that one pipeline.</summary>
    Task<Result<PagedList<PipelineExecutionSummaryDto>>> ListAsync(
        string broadcasterId,
        PaginationParams pagination,
        bool failuresOnly,
        Guid? pipelineId = null,
        CancellationToken ct = default
    );

    /// <summary>One run's detail, including its ordered per-step logs.</summary>
    Task<Result<PipelineExecutionDetailDto>> GetDetailAsync(
        string broadcasterId,
        long id,
        CancellationToken ct = default
    );
}
