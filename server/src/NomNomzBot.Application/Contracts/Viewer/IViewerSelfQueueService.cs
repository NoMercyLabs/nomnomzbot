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

namespace NomNomzBot.Application.Contracts.Viewer;

/// <summary>The signed-in viewer's own songs in a channel's song-request queue.</summary>
public interface IViewerSelfQueueService
{
    /// <summary>The caller's songs with their place in line; <c>NOT_FOUND</c> when the caller has no account.</summary>
    Task<Result<ViewerQueueDto>> GetMyQueueAsync(
        Guid channelId,
        Guid callerUserId,
        CancellationToken ct = default
    );
}
