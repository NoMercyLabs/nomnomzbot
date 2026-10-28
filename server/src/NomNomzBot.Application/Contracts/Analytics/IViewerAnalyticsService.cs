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

namespace NomNomzBot.Application.Contracts.Analytics;

/// <summary>Per-viewer analytics reads + the viewer-controlled opt-out (analytics.md §3.2).</summary>
public interface IViewerAnalyticsService
{
    /// <summary>One viewer's aggregate profile (M.1) for this channel; NOT_FOUND if the viewer never appeared.</summary>
    Task<Result<ViewerProfileDto>> GetProfileAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        CancellationToken ct = default
    );

    /// <summary>Ranked/filtered, paged viewer list (M.1).</summary>
    Task<Result<PagedList<ViewerProfileListItemDto>>> ListProfilesAsync(
        Guid broadcasterId,
        ViewerProfileQuery query,
        PaginationParams paging,
        CancellationToken ct = default
    );

    /// <summary>One viewer's daily engagement series (M.7) over an inclusive channel-local date range.</summary>
    Task<Result<IReadOnlyList<ViewerEngagementDailyDto>>> GetEngagementSeriesAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    );

    /// <summary>One viewer's attendance streak (M.3); NOT_FOUND if the viewer has no streak yet.</summary>
    Task<Result<WatchStreakDto>> GetStreakAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        CancellationToken ct = default
    );

    /// <summary>Viewer-controlled opt-out of per-viewer analytics (sets M.1 flag; M.8 unaffected). Idempotent.</summary>
    Task<Result> SetAnalyticsOptOutAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        bool optedOut,
        CancellationToken ct = default
    );
}
