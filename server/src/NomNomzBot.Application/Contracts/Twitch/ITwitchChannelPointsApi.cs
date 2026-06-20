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
/// The Helix "Channel Points" category sub-client: custom-reward CRUD and reward-redemption queries /
/// status updates (twitch-helix.md §3.2). One of the grouped sub-clients exposed by
/// <see cref="ITwitchHelixClient"/>. Every method takes the owning tenant as a <see cref="Guid"/> and
/// resolves it to the Twitch id internally (the invariant: a Guid never reaches Twitch). Each returns
/// <see cref="Result"/>/<see cref="Result{T}"/> carrying a closed <see cref="TwitchErrorCodes"/> on failure.
/// All endpoints use the broadcaster's user token; the app that created a reward is the only app that may
/// read, update, or delete it or its redemptions.
/// </summary>
public interface ITwitchChannelPointsApi
{
    /// <summary>Create Custom Reward — adds a channel-points reward to the channel. Requires <c>channel:manage:redemptions</c>.</summary>
    Task<Result<TwitchCustomReward>> CreateCustomRewardAsync(
        Guid broadcasterId,
        CreateCustomRewardRequest request,
        CancellationToken ct = default
    );

    /// <summary>Update Custom Reward — patches the reward identified by <paramref name="rewardId"/>. Requires <c>channel:manage:redemptions</c>.</summary>
    Task<Result<TwitchCustomReward>> UpdateCustomRewardAsync(
        Guid broadcasterId,
        string rewardId,
        UpdateCustomRewardRequest request,
        CancellationToken ct = default
    );

    /// <summary>Delete Custom Reward — removes the reward identified by <paramref name="rewardId"/>. Requires <c>channel:manage:redemptions</c>.</summary>
    Task<Result> DeleteCustomRewardAsync(
        Guid broadcasterId,
        string rewardId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Get Custom Rewards — the channel's custom rewards, optionally filtered to specific reward ids and/or
    /// to only the rewards this app can manage. Requires <c>channel:read:redemptions</c>.
    /// </summary>
    Task<Result<IReadOnlyList<TwitchCustomReward>>> GetCustomRewardsAsync(
        Guid broadcasterId,
        IReadOnlyList<string>? rewardIds = null,
        bool onlyManageableRewards = false,
        CancellationToken ct = default
    );

    /// <summary>
    /// Get Custom Reward Redemptions — one page of redemptions for a reward, selected either by lifecycle
    /// <paramref name="status"/> (CANCELED / FULFILLED / UNFULFILLED) or by specific redemption ids, with an
    /// optional OLDEST/NEWEST sort. Requires <c>channel:read:redemptions</c>.
    /// </summary>
    Task<Result<TwitchPage<TwitchCustomRewardRedemption>>> GetCustomRewardRedemptionsAsync(
        Guid broadcasterId,
        string rewardId,
        string? status,
        IReadOnlyList<string>? redemptionIds,
        string? sort,
        TwitchPageRequest page,
        CancellationToken ct = default
    );

    /// <summary>
    /// Update Redemption Status — moves the given UNFULFILLED redemptions of a reward to FULFILLED or
    /// CANCELED and returns the updated redemptions. Requires <c>channel:manage:redemptions</c>.
    /// </summary>
    Task<Result<IReadOnlyList<TwitchCustomRewardRedemption>>> UpdateRedemptionStatusAsync(
        Guid broadcasterId,
        string rewardId,
        IReadOnlyList<string> redemptionIds,
        UpdateRedemptionStatusRequest request,
        CancellationToken ct = default
    );
}
