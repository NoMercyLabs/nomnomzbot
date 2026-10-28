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

    /// <summary>Maps requester display names (the queue carries names only) to Twitch id and chat color; a name no viewer matches is absent.</summary>
    Task<Dictionary<string, RequesterIdentityDto>> ResolveRequestersAsync(
        IReadOnlyCollection<string> displayNames,
        CancellationToken cancellationToken = default
    );
}
