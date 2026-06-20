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

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// The Helix "Schedule" category sub-client: the broadcaster's streaming schedule, its segments and the
/// vacation window (twitch-helix.md §3.2). One of the grouped sub-clients exposed by
/// <see cref="ITwitchHelixClient"/>. Every method takes the owning tenant as a <see cref="Guid"/> and
/// resolves it to the Twitch id internally (the invariant: a Guid never reaches Twitch). Each returns
/// <see cref="Result"/>/<see cref="Result{T}"/> carrying a closed <see cref="TwitchErrorCodes"/> on failure.
/// </summary>
public interface ITwitchScheduleApi
{
    /// <summary>
    /// Get Channel Stream Schedule — one page of the broadcaster's schedule (a single nested schedule
    /// object carrying its segments). App token; no scope.
    /// </summary>
    Task<Result<TwitchSchedule>> GetScheduleAsync(
        Guid broadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    );

    /// <summary>
    /// Update Channel Stream Schedule — the schedule settings (toggle / schedule a vacation). Status-only.
    /// Requires <c>channel:manage:schedule</c>.
    /// </summary>
    Task<Result> UpdateScheduleSettingsAsync(
        Guid broadcasterId,
        bool? isVacationEnabled,
        DateTimeOffset? vacationStartTime,
        DateTimeOffset? vacationEndTime,
        string? timezone,
        CancellationToken ct = default
    );

    /// <summary>
    /// Create Channel Stream Schedule Segment — adds a single or recurring broadcast, returning the updated
    /// schedule. Requires <c>channel:manage:schedule</c>.
    /// </summary>
    Task<Result<TwitchSchedule>> CreateSegmentAsync(
        Guid broadcasterId,
        CreateScheduleSegmentRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// Update Channel Stream Schedule Segment — edits a scheduled segment, returning the updated schedule.
    /// Requires <c>channel:manage:schedule</c>.
    /// </summary>
    Task<Result<TwitchSchedule>> UpdateSegmentAsync(
        Guid broadcasterId,
        string segmentId,
        UpdateScheduleSegmentRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// Delete Channel Stream Schedule Segment — removes a segment from the schedule. Status-only.
    /// Requires <c>channel:manage:schedule</c>.
    /// </summary>
    Task<Result> DeleteSegmentAsync(
        Guid broadcasterId,
        string segmentId,
        CancellationToken ct = default
    );
}
