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
/// The Helix "Raids" category sub-client: start and cancel a raid that sends the broadcaster's viewers to
/// another channel (twitch-helix.md §3.2). One of the grouped sub-clients exposed by <see cref="ITwitchHelixClient"/>.
/// Every method takes the owning tenant as a <see cref="Guid"/> and resolves it to the Twitch id internally
/// (the invariant: a Guid never reaches Twitch); the raid target is a raw Twitch broadcaster id string.
/// Each returns <see cref="Result"/>/<see cref="Result{T}"/> carrying a closed <see cref="TwitchErrorCodes"/> on failure.
/// </summary>
public interface ITwitchRaidsApi
{
    /// <summary>
    /// Start a Raid — send the tenant's viewers to the target channel (raw Twitch broadcaster id). Returns the
    /// pending raid (created-at, mature flag). Requires <c>channel:manage:raids</c>.
    /// </summary>
    Task<Result<TwitchRaid>> StartRaidAsync(
        Guid broadcasterId,
        string toTwitchBroadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Cancel a Raid — cancel the tenant's pending raid. Requires <c>channel:manage:raids</c>.</summary>
    Task<Result> CancelRaidAsync(Guid broadcasterId, CancellationToken ct = default);
}
