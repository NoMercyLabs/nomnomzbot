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
