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
