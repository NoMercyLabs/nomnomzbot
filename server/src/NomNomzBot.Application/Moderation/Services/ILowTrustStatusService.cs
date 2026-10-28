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
