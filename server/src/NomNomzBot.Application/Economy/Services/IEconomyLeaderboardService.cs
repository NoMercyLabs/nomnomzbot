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
using NomNomzBot.Application.DTOs.Economy;

namespace NomNomzBot.Application.Economy.Services;

/// <summary>
/// Economy leaderboards (economy.md §3.8) — channel + jar rankings with a respected GDPR opt-out. Configs are
/// CRUD; rankings are computed live from the account projections (excluding opted-out viewers); closed periods
/// are frozen into append-only snapshots.
/// </summary>
public interface IEconomyLeaderboardService
{
    Task<Result<IReadOnlyList<LeaderboardConfigDto>>> ListConfigsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Creates (null Id) or updates a leaderboard config.</summary>
    Task<Result<LeaderboardConfigDto>> UpsertConfigAsync(
        Guid broadcasterId,
        UpsertLeaderboardConfigRequest request,
        CancellationToken ct = default
    );

    Task<Result> DeleteConfigAsync(
        Guid broadcasterId,
        Guid configId,
        CancellationToken ct = default
    );

    /// <summary>Live ranking for the config's metric, excluding opted-out viewers; top <c>TopN</c> (or override).</summary>
    Task<Result<IReadOnlyList<LeaderboardEntryDto>>> GetRankingAsync(
        Guid broadcasterId,
        Guid configId,
        int? top,
        CancellationToken ct = default
    );

    /// <summary>Freezes the current standings for a closed period into append-only snapshots.</summary>
    Task<Result<IReadOnlyList<LeaderboardEntryDto>>> CaptureSnapshotAsync(
        Guid broadcasterId,
        Guid configId,
        string periodKey,
        CancellationToken ct = default
    );

    /// <summary>GDPR opt-out: excludes the viewer from all live rankings going forward. Idempotent.</summary>
    Task<Result> OptOutAsync(Guid broadcasterId, Guid viewerUserId, CancellationToken ct = default);

    /// <summary>Removes the opt-out (re-includes the viewer). Idempotent.</summary>
    Task<Result> OptInAsync(Guid broadcasterId, Guid viewerUserId, CancellationToken ct = default);
}
