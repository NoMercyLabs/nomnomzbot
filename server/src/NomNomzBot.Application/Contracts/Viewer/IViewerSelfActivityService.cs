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

/// <summary>The signed-in viewer's own chat lines and runnable commands in one channel. Bound to the caller's user id.</summary>
public interface IViewerSelfActivityService
{
    /// <summary>The caller's own recent chat lines in the channel, newest first.</summary>
    Task<Result<PagedList<ViewerMessageDto>>> ListMessagesAsync(
        Guid channelId,
        Guid callerUserId,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    /// <summary>The channel's commands the caller's effective level may run, by name, with the caller's own last use.</summary>
    Task<Result<IReadOnlyList<ViewerCommandDto>>> ListCommandsAsync(
        Guid channelId,
        Guid callerUserId,
        CancellationToken ct = default
    );
}
