// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Domain.Rewards.Entities;

namespace NomNomzBot.Infrastructure.Rewards;

public class RewardService : IRewardService
{
    private readonly IApplicationDbContext _db;
    private readonly ITwitchChannelPointsApi _channelPoints;
    private readonly ILogger<RewardService> _logger;

    public RewardService(
        IApplicationDbContext db,
        ITwitchChannelPointsApi channelPoints,
        ILogger<RewardService> logger
    )
    {
        _db = db;
        _channelPoints = channelPoints;
        _logger = logger;
    }

    public async Task<Result<RewardDetail>> CreateAsync(
        string broadcasterId,
        CreateRewardRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<RewardDetail>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        bool channel = await _db.Channels.AnyAsync(c => c.Id == broadcaster, cancellationToken);

        if (!channel)
            return Errors.ChannelNotFound<RewardDetail>(broadcasterId);

        Reward reward = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = broadcaster,
            Title = request.Title,
            IsEnabled = true,
        };

        _db.Rewards.Add(reward);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDetail(reward));
    }

    public async Task<Result<RewardDetail>> UpdateAsync(
        string broadcasterId,
        string rewardId,
        UpdateRewardRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<RewardDetail>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        if (!Guid.TryParse(rewardId, out Guid guid))
            return Result.Failure<RewardDetail>(
                $"Invalid reward ID '{rewardId}'.",
                "VALIDATION_FAILED"
            );

        Reward? reward = await _db.Rewards.FirstOrDefaultAsync(
            r => r.Id == guid && r.BroadcasterId == broadcaster,
            cancellationToken
        );

        if (reward is null)
            return Errors.NotFound<RewardDetail>("Reward", rewardId);

        if (request.Title is not null)
            reward.Title = request.Title;
        if (request.IsEnabled.HasValue)
            reward.IsEnabled = request.IsEnabled.Value;

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDetail(reward));
    }

    public async Task<Result> DeleteAsync(
        string broadcasterId,
        string rewardId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure($"Invalid channel ID '{broadcasterId}'.", "VALIDATION_FAILED");

        if (!Guid.TryParse(rewardId, out Guid guid))
            return Result.Failure($"Invalid reward ID '{rewardId}'.", "VALIDATION_FAILED");

        Reward? reward = await _db.Rewards.FirstOrDefaultAsync(
            r => r.Id == guid && r.BroadcasterId == broadcaster,
            cancellationToken
        );

        if (reward is null)
            return Result.Failure($"Reward '{rewardId}' was not found.", "NOT_FOUND");

        _db.Rewards.Remove(reward);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<PagedList<RewardListItem>>> ListAsync(
        string broadcasterId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<PagedList<RewardListItem>>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        IQueryable<Reward> query = _db.Rewards.Where(r => r.BroadcasterId == broadcaster);
        int total = await query.CountAsync(cancellationToken);

        List<RewardListItem> items = await query
            .OrderBy(r => r.Title)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(r => new RewardListItem(
                r.Id.ToString(),
                r.Title,
                r.Cost ?? 0,
                r.IsEnabled,
                null,
                null,
                r.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        return Result.Success(
            new PagedList<RewardListItem>(items, total, pagination.Page, pagination.PageSize)
        );
    }

    public async Task<Result<RewardDetail>> GetAsync(
        string broadcasterId,
        string rewardId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<RewardDetail>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        if (!Guid.TryParse(rewardId, out Guid guid))
            return Result.Failure<RewardDetail>(
                $"Invalid reward ID '{rewardId}'.",
                "VALIDATION_FAILED"
            );

        Reward? reward = await _db.Rewards.FirstOrDefaultAsync(
            r => r.Id == guid && r.BroadcasterId == broadcaster,
            cancellationToken
        );

        if (reward is null)
            return Errors.NotFound<RewardDetail>("Reward", rewardId);

        return Result.Success(ToDetail(reward));
    }

    public async Task<Result> SyncWithTwitchAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure($"Invalid channel ID '{broadcasterId}'.", "VALIDATION_FAILED");

        bool channelExists = await _db.Channels.AnyAsync(
            c => c.Id == broadcaster,
            cancellationToken
        );
        if (!channelExists)
            return Errors.ChannelNotFound(broadcasterId);

        // Reconcile the bot's MANAGED rewards (rewards.md §3.1): Helix returns only rewards this client_id
        // created (`only_manageable_rewards=true`). A freshly-onboarded channel where the bot has created
        // none legitimately yields an empty set — unmanaged (streamer-created) rewards are NOT pulled here;
        // they surface at redemption time. A *failed* read (no token / missing scope / Twitch error) is a
        // different thing entirely and must not masquerade as "no rewards": it is logged with its real Helix
        // error code and propagated so the onboarding seed handler and dashboard see the actual cause.
        Result<IReadOnlyList<TwitchCustomReward>> rewardsResult =
            await _channelPoints.GetCustomRewardsAsync(
                broadcaster,
                onlyManageableRewards: true,
                ct: cancellationToken
            );
        if (rewardsResult.IsFailure)
        {
            _logger.LogWarning(
                "Reward sync: reading channel-point rewards from Twitch failed for {BroadcasterId}: {Error} ({Code}){Detail}",
                broadcasterId,
                rewardsResult.ErrorMessage,
                rewardsResult.ErrorCode,
                rewardsResult.ErrorDetail is null ? "" : $" — {rewardsResult.ErrorDetail}"
            );
            return rewardsResult;
        }

        IReadOnlyList<TwitchCustomReward> twitchRewards = rewardsResult.Value;
        if (twitchRewards.Count == 0)
        {
            _logger.LogInformation(
                "Reward sync: Twitch returned no bot-managed rewards for broadcaster {BroadcasterId} "
                    + "(streamer-created rewards are unmanaged and surface at redemption time, not via sync)",
                broadcasterId
            );
            return Result.Success();
        }

        List<Reward> existing = await _db
            .Rewards.Where(r => r.BroadcasterId == broadcaster)
            .ToListAsync(cancellationToken);

        Dictionary<string, Reward> existingByTwitchId = existing
            .Where(r => r.TwitchRewardId != null)
            .ToDictionary(r => r.TwitchRewardId!);

        Dictionary<string, Reward> existingByTitle = existing.ToDictionary(
            r => r.Title,
            StringComparer.OrdinalIgnoreCase
        );

        int syncedCount = 0;
        foreach (TwitchCustomReward tr in twitchRewards)
        {
            if (existingByTwitchId.TryGetValue(tr.Id, out Reward? reward))
            {
                // Update existing record
                reward.Title = tr.Title;
                reward.Cost = tr.Cost;
                reward.IsEnabled = tr.IsEnabled;
                reward.Description = tr.Prompt;
                syncedCount++;
            }
            else if (existingByTitle.TryGetValue(tr.Title, out Reward? rewardByTitle))
            {
                // Match by title — link Twitch ID
                rewardByTitle.TwitchRewardId = tr.Id;
                rewardByTitle.Cost = tr.Cost;
                rewardByTitle.IsEnabled = tr.IsEnabled;
                rewardByTitle.Description = tr.Prompt;
                syncedCount++;
            }
            else
            {
                // New reward — create local record
                _db.Rewards.Add(
                    new()
                    {
                        Id = Guid.NewGuid(),
                        BroadcasterId = broadcaster,
                        Title = tr.Title,
                        TwitchRewardId = tr.Id,
                        Cost = tr.Cost,
                        IsEnabled = tr.IsEnabled,
                        Description = tr.Prompt,
                        IsPlatform = true,
                    }
                );
                syncedCount++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Synced {Count} rewards for broadcaster {BroadcasterId}",
            syncedCount,
            broadcasterId
        );
        return Result.Success();
    }

    private static RewardDetail ToDetail(Reward r) =>
        new(
            r.Id.ToString(),
            r.Title,
            r.Description,
            r.Cost ?? 0,
            r.IsEnabled,
            false,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            r.CreatedAt,
            r.UpdatedAt
        );
}
