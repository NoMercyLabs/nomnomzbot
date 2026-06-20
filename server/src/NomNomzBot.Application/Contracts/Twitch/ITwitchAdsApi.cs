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
