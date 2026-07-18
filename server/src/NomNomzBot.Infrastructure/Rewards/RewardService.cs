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
            TimerDurationSeconds = NormalizeTimerDuration(request.TimerDurationSeconds),
            PipelineId = request.PipelineId,
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
        if (request.TimerDurationSeconds.HasValue)
            reward.TimerDurationSeconds = NormalizeTimerDuration(request.TimerDurationSeconds);
        // Absent leaves the binding unchanged; Guid.Empty clears it; a real id binds that pipeline.
        if (request.PipelineId.HasValue)
            reward.PipelineId =
                request.PipelineId.Value == Guid.Empty ? null : request.PipelineId.Value;

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

    public async Task<Result<PagedList<RewardDetail>>> ListAsync(
        string broadcasterId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<PagedList<RewardDetail>>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        IQueryable<Reward> query = _db.Rewards.Where(r => r.BroadcasterId == broadcaster);
        int total = await query.CountAsync(cancellationToken);

        List<Reward> rewards = await query
            .OrderBy(r => r.Title)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);

        // Project to the full RewardDetail the controller declares (PaginatedResponse<RewardDetail>) — a list row
        // carries the SAME shape as get/create, including the viewer-facing Prompt (Reward.Description). The old
        // RewardListItem projection silently dropped Prompt (and the other detail fields) from the list JSON, so
        // the dashboard never saw the prompt an operator set on Twitch.
        List<RewardDetail> items = rewards.Select(ToDetail).ToList();

        return Result.Success(
            new PagedList<RewardDetail>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<PagedList<RedemptionListItem>>> ListRedemptionsAsync(
        string broadcasterId,
        string? status,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<PagedList<RedemptionListItem>>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        IQueryable<Redemption> query = _db.Redemptions.Where(r => r.BroadcasterId == broadcaster);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status);

        int total = await query.CountAsync(cancellationToken);

        List<RedemptionListItem> items = await query
            .OrderByDescending(r => r.RedeemedAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(r => new RedemptionListItem(
                r.RedemptionId,
                r.RewardId,
                r.RewardTitle,
                r.UserId,
                r.UserDisplayName,
                r.Cost,
                r.UserInput,
                r.Status,
                r.RedeemedAt
            ))
            .ToListAsync(cancellationToken);

        // PagedList has two ctors with DIFFERENT arg orders; a List<T> binds to the (items, page, pageSize,
        // totalCount) one, so pass in THAT order — (items, total, page, pageSize) silently sets TotalCount to the
        // page size.
        return Result.Success(
            new PagedList<RedemptionListItem>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result> SetRedemptionStatusAsync(
        string broadcasterId,
        string redemptionId,
        string twitchStatus,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure($"Invalid channel ID '{broadcasterId}'.", "VALIDATION_FAILED");

        // The reward id Helix needs to address the redemption rides the queue read model (folded from the journal).
        Redemption? row = await _db.Redemptions.FirstOrDefaultAsync(
            r => r.BroadcasterId == broadcaster && r.RedemptionId == redemptionId,
            cancellationToken
        );
        if (row is null)
            return Result.Failure($"Redemption '{redemptionId}' was not found.", "NOT_FOUND");

        Result<IReadOnlyList<TwitchCustomRewardRedemption>> helix =
            await _channelPoints.UpdateRedemptionStatusAsync(
                broadcaster,
                row.RewardId,
                [redemptionId],
                new UpdateRedemptionStatusRequest(twitchStatus),
                cancellationToken
            );
        if (helix.IsFailure)
            return Result.Failure(
                helix.ErrorMessage ?? "Twitch rejected the redemption update.",
                helix.ErrorCode ?? "TWITCH_ERROR"
            );

        // Optimistic local update so the queue re-list drops it from the pending lane immediately; the matching
        // EventSub redemption.update folds the same status through the projection (idempotent), confirming it.
        row.Status = twitchStatus.Equals("FULFILLED", StringComparison.OrdinalIgnoreCase)
            ? "fulfilled"
            : "canceled";
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
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

        // Sync read `only_manageable_rewards=true`, so every reward it received is one THIS client can manage —
        // the whole returned set IS the manageable id set.
        HashSet<string> manageableRewardIds = twitchRewards
            .Select(r => r.Id)
            .ToHashSet(StringComparer.Ordinal);

        int syncedCount = await UpsertTwitchRewardsAsync(
            broadcaster,
            twitchRewards,
            manageableRewardIds,
            cancellationToken
        );

        _logger.LogInformation(
            "Synced {Count} rewards for broadcaster {BroadcasterId}",
            syncedCount,
            broadcasterId
        );
        return Result.Success();
    }

    public async Task<Result> ImportFromTwitchAsync(
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

        // Import pulls the FULL reward set (rewards.md §3.1) — `only_manageable_rewards=false` — so
        // externally-created rewards (Twitch UI / other apps) come across too, each carrying Twitch's
        // `is_manageable` flag. A failed read is surfaced with its real Helix code (never masqueraded as
        // "no rewards"); a genuine empty set is a success with nothing imported.
        Result<IReadOnlyList<TwitchCustomReward>> rewardsResult =
            await _channelPoints.GetCustomRewardsAsync(
                broadcaster,
                onlyManageableRewards: false,
                ct: cancellationToken
            );
        if (rewardsResult.IsFailure)
        {
            _logger.LogWarning(
                "Reward import: reading channel-point rewards from Twitch failed for {BroadcasterId}: {Error} ({Code}){Detail}",
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
                "Reward import: Twitch returned no channel-point rewards for broadcaster {BroadcasterId}",
                broadcasterId
            );
            return Result.Success();
        }

        // Manageability is NOT a field on the reward payload — it is the `only_manageable_rewards=true` subset.
        // A second read gives the ids THIS client can manage; a reward is stored manageable iff its id is in it.
        // A failed read must NOT be swallowed into "everything unmanaged" (the bug this fix removes) — surface
        // the real Helix error so the import fails loudly rather than persisting wrong manageability.
        Result<IReadOnlyList<TwitchCustomReward>> manageableResult =
            await _channelPoints.GetCustomRewardsAsync(
                broadcaster,
                onlyManageableRewards: true,
                ct: cancellationToken
            );
        if (manageableResult.IsFailure)
        {
            _logger.LogWarning(
                "Reward import: reading the bot-manageable reward subset from Twitch failed for {BroadcasterId}: {Error} ({Code}){Detail}",
                broadcasterId,
                manageableResult.ErrorMessage,
                manageableResult.ErrorCode,
                manageableResult.ErrorDetail is null ? "" : $" — {manageableResult.ErrorDetail}"
            );
            return manageableResult;
        }

        HashSet<string> manageableRewardIds = manageableResult
            .Value.Select(r => r.Id)
            .ToHashSet(StringComparer.Ordinal);

        int importedCount = await UpsertTwitchRewardsAsync(
            broadcaster,
            twitchRewards,
            manageableRewardIds,
            cancellationToken
        );

        _logger.LogInformation(
            "Imported {Count} rewards (managed + external) for broadcaster {BroadcasterId}",
            importedCount,
            broadcasterId
        );
        return Result.Success();
    }

    public async Task<Result<RewardDetail>> RecreateUnderBotAsync(
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

        Reward? external = await _db.Rewards.FirstOrDefaultAsync(
            r => r.Id == guid && r.BroadcasterId == broadcaster,
            cancellationToken
        );

        if (external is null)
            return Errors.NotFound<RewardDetail>("Reward", rewardId);

        // Twitch only lets a client manage rewards ITS OWN client_id created. A reward we already manage has
        // nothing to convert — recreating it would just duplicate it under the same client. ALREADY_EXISTS maps
        // to 409 Conflict (BaseController.ResultResponse), the correct signal for "already in the target state".
        if (external.IsManageable)
            return Result.Failure<RewardDetail>(
                "This reward is already managed by the bot; there is nothing to convert.",
                "ALREADY_EXISTS"
            );

        // We cannot take over the original (another client_id owns it), so we recreate an equivalent reward
        // under the bot's client. The new reward gets its own Twitch id and IS manageable.
        Result<TwitchCustomReward> created = await _channelPoints.CreateCustomRewardAsync(
            broadcaster,
            new CreateCustomRewardRequest(
                Title: external.Title,
                Cost: external.Cost ?? 0,
                Prompt: external.Description,
                IsEnabled: external.IsEnabled
            ),
            cancellationToken
        );
        if (created.IsFailure)
            return created.WithValue<RewardDetail>(default!);

        TwitchCustomReward tr = created.Value;
        Reward botReward = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = broadcaster,
            Title = tr.Title,
            Description = tr.Prompt,
            Cost = tr.Cost,
            IsEnabled = tr.IsEnabled,
            TwitchRewardId = tr.Id,
            IsPlatform = true,
            IsManageable = true,
        };

        // A single insert (the original external row is left exactly as-is) — one SaveChanges is atomic,
        // matching how the rest of this service persists.
        _db.Rewards.Add(botReward);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Recreated external reward '{Title}' ({ExternalId}) under the bot as {TwitchRewardId} for {BroadcasterId}",
            external.Title,
            external.TwitchRewardId,
            tr.Id,
            broadcasterId
        );
        return Result.Success(ToDetail(botReward));
    }

    /// <summary>
    /// Upserts a set of Twitch rewards into the local table, matching first by Twitch id, then by title
    /// (linking a locally-created reward), else creating a new row. A reward is manageable iff its Twitch id is
    /// in <paramref name="manageableRewardIds"/> — the id set Twitch returns for
    /// <c>only_manageable_rewards=true</c>, i.e. the rewards THIS client_id created. That drives both the
    /// persisted <see cref="Reward.IsManageable"/> (external rewards recorded read-only) and the platform flag
    /// on a newly-created row. Twitch's reward payload carries no manageability field, so it is never inferred
    /// from the wire. Shared by sync (managed set only) and import (full set). Returns the number upserted.
    /// </summary>
    private async Task<int> UpsertTwitchRewardsAsync(
        Guid broadcaster,
        IReadOnlyList<TwitchCustomReward> twitchRewards,
        IReadOnlySet<string> manageableRewardIds,
        CancellationToken cancellationToken
    )
    {
        List<Reward> existing = await _db
            .Rewards.Where(r => r.BroadcasterId == broadcaster)
            .ToListAsync(cancellationToken);

        Dictionary<string, Reward> existingByTwitchId = existing
            .Where(r => r.TwitchRewardId != null)
            .ToDictionary(r => r.TwitchRewardId!);

        // Title-match is a fallback for linking a locally-created reward (no Twitch id yet). Titles are NOT
        // unique — once a reward is recreated under the bot, its original external row and the new bot row share
        // a title — so group and keep the first candidate rather than letting ToDictionary throw on a duplicate.
        Dictionary<string, Reward> existingByTitle = existing
            .Where(r => r.TwitchRewardId is null)
            .GroupBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        int upsertedCount = 0;
        foreach (TwitchCustomReward tr in twitchRewards)
        {
            bool manageable = manageableRewardIds.Contains(tr.Id);
            if (existingByTwitchId.TryGetValue(tr.Id, out Reward? reward))
            {
                // Update existing record
                reward.Title = tr.Title;
                reward.Cost = tr.Cost;
                reward.IsEnabled = tr.IsEnabled;
                reward.Description = tr.Prompt;
                reward.IsManageable = manageable;
                upsertedCount++;
            }
            else if (existingByTitle.TryGetValue(tr.Title, out Reward? rewardByTitle))
            {
                // Match by title — link Twitch ID
                rewardByTitle.TwitchRewardId = tr.Id;
                rewardByTitle.Cost = tr.Cost;
                rewardByTitle.IsEnabled = tr.IsEnabled;
                rewardByTitle.Description = tr.Prompt;
                rewardByTitle.IsManageable = manageable;
                upsertedCount++;
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
                        IsPlatform = manageable,
                        IsManageable = manageable,
                    }
                );
                upsertedCount++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return upsertedCount;
    }

    /// <summary>Clamps a requested countdown to sane bounds: 0/negative clears it; the ceiling is 24h.</summary>
    private static int? NormalizeTimerDuration(int? requested) =>
        requested is int seconds && seconds > 0 ? Math.Min(seconds, 86_400) : null;

    private static RewardDetail ToDetail(Reward r) =>
        new(
            r.Id.ToString(),
            r.Title,
            r.Description,
            r.Cost ?? 0,
            r.IsEnabled,
            r.IsManageable,
            false,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            r.TimerDurationSeconds,
            r.PipelineId,
            r.CreatedAt,
            r.UpdatedAt
        );
}
