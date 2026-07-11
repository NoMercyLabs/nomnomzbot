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
using NomNomzBot.Application.Engagement.Dtos;

namespace NomNomzBot.Application.Engagement.Services;

/// <summary>
/// Detects the three engagement moments from the chat stream and fires them as pipeline triggers
/// (engagement.md §3). Owns the per-viewer state needed to detect them; adds no greeting policy — the
/// bound pipeline decides.
/// </summary>
public interface IEngagementService
{
    /// <summary>
    /// The chat hot-path hook — called once per inbound chat message WHILE LIVE. Upserts the viewer's
    /// engagement state in one transaction and fires at most one engagement event per the rules
    /// (D1/D3/D4):
    /// <list type="bullet">
    ///   <item>no prior row → <c>FirstTimeChatterDetectedEvent</c></item>
    ///   <item>prior row, new stream → <c>ReturningChatterDetectedEvent</c> (+ streak update; a milestone
    ///   also fires <c>WatchStreakMilestoneEvent</c>)</item>
    ///   <item>same stream / on the greet cooldown → state update only, no event</item>
    /// </list>
    /// Fast return when the matching trigger is disabled in <c>EngagementConfig</c>.
    /// </summary>
    Task<Result> OnChatActivityAsync(
        Guid broadcasterId,
        EngagementSignal signal,
        CancellationToken ct = default
    );

    Task<Result<EngagementConfigDto>> GetConfigAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    Task<Result<EngagementConfigDto>> UpdateConfigAsync(
        Guid broadcasterId,
        UpdateEngagementConfigRequest request,
        CancellationToken ct = default
    );
}
