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
/// The Helix "Hype Train" category sub-client: the current Hype Train status and the channel's records
/// (twitch-helix.md §3.2). One of the grouped sub-clients exposed by <see cref="ITwitchHelixClient"/>.
/// Every method takes the owning tenant as a <see cref="Guid"/> and resolves it to the Twitch id internally
/// (the invariant: a Guid never reaches Twitch). Each returns <see cref="Result"/>/<see cref="Result{T}"/>
/// carrying a closed <see cref="TwitchErrorCodes"/> on failure.
/// </summary>
public interface ITwitchHypeTrainApi
{
    /// <summary>
    /// Get Hype Train Status — the broadcaster's current Hype Train (level, total, progress, goal, top
    /// contributors, shared participants, timing) plus its all-time and shared all-time records. Requires
    /// <c>channel:read:hype_train</c>. Resolves to <c>not_found</c> when the channel has no Hype Train activity.
    /// </summary>
    Task<Result<TwitchHypeTrainStatus>> GetHypeTrainStatusAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );
}
