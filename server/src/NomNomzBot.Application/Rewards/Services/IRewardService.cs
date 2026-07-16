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
using NomNomzBot.Application.Rewards.Dtos;

namespace NomNomzBot.Application.Rewards.Services;

/// <summary>
/// Application service for managing channel point rewards and their actions.
/// </summary>
public interface IRewardService
{
    /// <summary>Create a new reward.</summary>
    Task<Result<RewardDetail>> CreateAsync(
        string broadcasterId,
        CreateRewardRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Update an existing reward.</summary>
    Task<Result<RewardDetail>> UpdateAsync(
        string broadcasterId,
        string rewardId,
        UpdateRewardRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Delete a reward.</summary>
    Task<Result> DeleteAsync(
        string broadcasterId,
        string rewardId,
        CancellationToken cancellationToken = default
    );

    /// <summary>List all rewards for a channel with pagination.</summary>
    Task<Result<PagedList<RewardDetail>>> ListAsync(
        string broadcasterId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// The channel-points redemption queue for a channel (newest first), optionally filtered to a single
    /// <paramref name="status"/> (<c>unfulfilled</c> for the pending queue). Reads the journal-folded read model.
    /// </summary>
    Task<Result<PagedList<RedemptionListItem>>> ListRedemptionsAsync(
        string broadcasterId,
        string? status,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Fulfil or refund a queued redemption via Helix (<paramref name="twitchStatus"/> = <c>FULFILLED</c> or
    /// <c>CANCELED</c>), then reflect the new status in the local queue read model. Addressed by the redemption id;
    /// the reward id Helix requires is resolved from the read model.
    /// </summary>
    Task<Result> SetRedemptionStatusAsync(
        string broadcasterId,
        string redemptionId,
        string twitchStatus,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get a single reward by ID.</summary>
    Task<Result<RewardDetail>> GetAsync(
        string broadcasterId,
        string rewardId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sync local rewards with the bot's MANAGED Twitch channel point rewards (only the rewards this
    /// client_id created). Reconciliation only — does not pull externally-created rewards.
    /// </summary>
    Task<Result> SyncWithTwitchAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Import ALL of the channel's Twitch channel-point rewards into the local table — including externally
    /// created ones (Twitch UI / other apps) that this client_id cannot manage. Each imported reward records
    /// whether the bot can manage it (<see cref="Domain.Rewards.Entities.Reward.IsManageable"/>), so the
    /// dashboard can offer edit/delete only where Twitch actually permits it. Upserts by Twitch id, then by
    /// title, else creates — the same reconciliation as sync, widened to the unmanaged set.
    /// </summary>
    Task<Result> ImportFromTwitchAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Convert an external (non-manageable) reward to bot-controlled by RECREATING an equivalent reward under
    /// the bot's own Twitch client — Twitch does not allow taking over a reward another client_id created.
    /// Copies title/cost/prompt/enabled to a new Helix reward, persists it as a second, bot-managed row (new
    /// Twitch id, <c>IsManageable = true</c>), and leaves the original external row untouched. Fails when the
    /// target reward is already bot-manageable (nothing to convert).
    /// </summary>
    Task<Result<RewardDetail>> RecreateUnderBotAsync(
        string broadcasterId,
        string rewardId,
        CancellationToken cancellationToken = default
    );
}
