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
using NomNomzBot.Application.Music.Dtos;

namespace NomNomzBot.Application.Music.Services;

/// <summary>Reads the channel's song-request history rows for the dashboard Music page.</summary>
public interface IMusicHistoryReader
{
    /// <summary>The most recent accepted requests, newest first, with the requester's display name.</summary>
    Task<Result<List<SongHistoryItemDto>>> GetRecentAsync(
        Guid broadcasterId,
        int limit,
        CancellationToken cancellationToken = default
    );

    /// <summary>The viewers with the most requests, highest count first.</summary>
    Task<Result<List<TopRequesterDto>>> GetTopRequestersAsync(
        Guid broadcasterId,
        int limit,
        CancellationToken cancellationToken = default
    );
}
