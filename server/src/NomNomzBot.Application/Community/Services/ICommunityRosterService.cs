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

namespace NomNomzBot.Application.Community.Services;

/// <summary>
/// Reconciles the channel's moderator roster (the <c>ChannelModerators</c> table the Community page reads)
/// from Twitch. The single idempotent upsert path: it pulls the channel's current moderators (and VIPs, which
/// the Community roster surfaces alongside mods) from Helix, get-or-creates a <c>User</c> per member, and
/// upserts the moderator rows — adding the newly-seen, leaving the already-present untouched. Safe to run
/// repeatedly (onboarding seed + every backfill), so a no-token / missing-scope channel simply seeds nothing.
/// </summary>
public interface ICommunityRosterService
{
    /// <summary>
    /// Pulls the channel's moderators + VIPs from Twitch and upserts the local moderator roster. Returns the
    /// number of moderator rows newly created (0 when nothing was missing or Twitch returned no roster).
    /// </summary>
    Task<Result<int>> SyncModeratorsFromTwitchAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );
}
