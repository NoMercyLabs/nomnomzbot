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
/// The Helix "Ads" category sub-client: start a commercial, read the ad schedule, and snooze the next ad
/// (twitch-helix.md §3.2). One of the grouped sub-clients exposed by <see cref="ITwitchHelixClient"/>.
/// Every method takes the owning tenant as a <see cref="Guid"/> and resolves it to the Twitch id internally
/// (the invariant: a Guid never reaches Twitch). Each returns <see cref="Result{T}"/> carrying a closed
/// <see cref="TwitchErrorCodes"/> on failure. All methods use the broadcaster's user token.
/// </summary>
public interface ITwitchAdsApi
{
    /// <summary>Start Commercial — runs a commercial of up to <paramref name="lengthSeconds"/> seconds. Requires <c>channel:edit:commercial</c>.</summary>
    Task<Result<TwitchCommercial>> StartCommercialAsync(
        Guid broadcasterId,
        int lengthSeconds,
        CancellationToken ct = default
    );

    /// <summary>Get Ad Schedule — next/last ad, current duration, pre-roll-free time, and snooze allowance. Requires <c>channel:read:ads</c>.</summary>
    Task<Result<TwitchAdSchedule>> GetAdScheduleAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Snooze Next Ad — pushes the upcoming mid-roll ad back by 5 minutes when an allowance remains. Requires <c>channel:manage:ads</c>.</summary>
    Task<Result<TwitchAdSnooze>> SnoozeNextAdAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );
}
