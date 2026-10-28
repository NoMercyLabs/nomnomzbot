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

/// <summary>Counts any requested metrics of one stream from recorded data inside the stream window.</summary>
public interface IStreamStatsService
{
    /// <summary>
    /// Stats of <paramref name="streamId"/>, or of the channel's latest stream (live or ended) when null, for the
    /// named <paramref name="metrics"/> (all when null or empty). Fails with <c>NOT_FOUND</c> when the channel has no
    /// such stream and <c>VALIDATION_FAILED</c> naming the first unknown metric key.
    /// </summary>
    Task<Result<StreamStatsDto>> GetAsync(
        Guid broadcasterId,
        string? streamId,
        IReadOnlyCollection<string>? metrics,
        CancellationToken cancellationToken = default
    );
}
