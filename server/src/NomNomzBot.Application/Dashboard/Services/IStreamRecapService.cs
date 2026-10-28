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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Dashboard.Dtos;

namespace NomNomzBot.Application.Dashboard.Services;

/// <summary>Builds a stream's recap and keeps it: one row per stream, read back newest first.</summary>
public interface IStreamRecapService
{
    /// <summary>
    /// Builds the recap of <paramref name="streamId"/> (the channel's latest stream when null) through
    /// <see cref="IStreamStatsService"/> and saves it, replacing the stream's earlier recap. Fails like the stats
    /// service does (<c>NOT_FOUND</c>, <c>VALIDATION_FAILED</c>); nothing is saved on a failure.
    /// </summary>
    Task<Result<StreamRecapDto>> BuildAsync(
        Guid broadcasterId,
        string? streamId,
        IReadOnlyCollection<string>? metrics,
        CancellationToken cancellationToken = default
    );

    /// <summary>The kept recaps of the channel, newest stream first.</summary>
    Task<Result<PagedList<StreamRecapDto>>> ListAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    );

    /// <summary>The kept recap of one stream, or <c>NOT_FOUND</c>.</summary>
    Task<Result<StreamRecapDto>> GetAsync(
        Guid broadcasterId,
        string streamId,
        CancellationToken cancellationToken = default
    );
}
