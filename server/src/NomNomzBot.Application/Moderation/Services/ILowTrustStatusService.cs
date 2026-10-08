// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// Keeps Twitch's per-channel suspicious-user flag (<c>none</c> / <c>active_monitoring</c> / <c>restricted</c>)
/// for each chatter, fed by <c>channel.suspicious_user.update</c> and <c>channel.suspicious_user.message</c>.
/// </summary>
public interface ILowTrustStatusService
{
    /// <summary>
    /// Store the chatter's flag; <c>none</c> clears it. Publishes a <c>suspicious-users</c> config push when the
    /// stored status changed. Returns true when it changed.
    /// </summary>
    Task<bool> ApplyAsync(
        Guid broadcasterId,
        string twitchUserId,
        string status,
        string? banEvasionEvaluation,
        CancellationToken cancellationToken = default
    );

    /// <summary>The chatter's current flag in the channel; <c>none</c> when there is none.</summary>
    Task<string> GetAsync(
        Guid broadcasterId,
        string twitchUserId,
        CancellationToken cancellationToken = default
    );
}
