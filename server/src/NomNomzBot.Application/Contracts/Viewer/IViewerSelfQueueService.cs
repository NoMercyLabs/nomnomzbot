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
