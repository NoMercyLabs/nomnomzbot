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
    Task<Result<PagedList<RewardListItem>>> ListAsync(
        string broadcasterId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get a single reward by ID.</summary>
    Task<Result<RewardDetail>> GetAsync(
        string broadcasterId,
        string rewardId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Sync local rewards with Twitch channel point rewards.</summary>
    Task<Result> SyncWithTwitchAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );
}
