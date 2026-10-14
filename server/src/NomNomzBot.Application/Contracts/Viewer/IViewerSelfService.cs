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

/// <summary>The signed-in viewer's own activity, per channel. Every read is bound to the caller's user id.</summary>
public interface IViewerSelfService
{
    /// <summary>Channels where the caller has viewer activity (never one they own), newest activity first.</summary>
    Task<Result<IReadOnlyList<WatchedChannelDto>>> ListWatchedChannelsAsync(
        Guid callerUserId,
        CancellationToken ct = default
    );

    /// <summary>The caller's own tiles in one channel; the zero/null shape when they have no activity there.</summary>
    Task<Result<ViewerSelfSummaryDto>> GetSummaryAsync(
        Guid channelId,
        Guid callerUserId,
        CancellationToken ct = default
    );
}
