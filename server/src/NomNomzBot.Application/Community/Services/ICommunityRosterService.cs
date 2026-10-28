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
