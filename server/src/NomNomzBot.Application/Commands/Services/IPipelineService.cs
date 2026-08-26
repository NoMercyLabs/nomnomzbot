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
