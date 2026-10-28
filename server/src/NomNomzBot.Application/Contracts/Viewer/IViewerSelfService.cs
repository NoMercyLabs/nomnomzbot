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
