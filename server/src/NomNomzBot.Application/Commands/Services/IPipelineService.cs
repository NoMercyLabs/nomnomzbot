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
/// Manages per-channel pipeline definitions created by the visual node builder.
/// </summary>
public interface IPipelineService
{
    Task<Result<PagedList<PipelineListItemDto>>> ListAsync(
        string broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    Task<Result<PipelineDto>> GetAsync(
        string broadcasterId,
        Guid id,
        CancellationToken ct = default
    );

    Task<Result<PipelineDto>> CreateAsync(
        string broadcasterId,
        CreatePipelineDto request,
        CancellationToken ct = default
    );

    Task<Result<PipelineDto>> UpdateAsync(
        string broadcasterId,
        Guid id,
        UpdatePipelineDto request,
        CancellationToken ct = default
    );

    Task<Result> DeleteAsync(string broadcasterId, Guid id, CancellationToken ct = default);

    /// <summary>
    /// Runs a graph through the exact save-time gate (wire parse plus the pipeline validator) without
    /// storing anything. Returns the wire JSON a save would persist, or the same typed failure a save
    /// returns. A null graph succeeds with null.
    /// </summary>
    Task<Result<string?>> ValidateGraphAsync(object? graph, CancellationToken ct = default);

    /// <summary>
    /// Real, counted rows (commands / chat triggers / timers / event responses) that reference this
    /// pipeline right now — for the delete confirmation, computed from the live tenant data, never
    /// estimated.
    /// </summary>
    Task<Result<PipelineBlastRadiusDto>> GetBlastRadiusAsync(
        string broadcasterId,
        Guid id,
        CancellationToken ct = default
    );
}
