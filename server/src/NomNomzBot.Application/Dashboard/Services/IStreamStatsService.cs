// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
