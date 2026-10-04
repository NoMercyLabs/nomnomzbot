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
using NomNomzBot.Application.Common.Consequences;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Infrastructure.Commands;

namespace NomNomzBot.Infrastructure.Rewards;

public class RewardService : IRewardService
{
    private readonly IApplicationDbContext _db;
    private readonly ITwitchChannelPointsApi _channelPoints;
    private readonly TimeProvider _clock;
    private readonly ILogger<RewardService> _logger;

    /// <summary>How many dependent names the preview lists by name before it just counts them.</summary>
    private const int SampleSize = 5;

    // How long a channel's imported-from-Twitch reward snapshot (Reward.IsManageable, PendingMigrationRequestedAt,
    // and the external rows themselves) is trusted before ListAsync re-syncs it in the background. Rewards change
    // rarely — a Helix round trip on every single page load would add real latency for no benefit — but a
    // streamer who creates or removes a reward on Twitch's own dashboard should see it reflected without needing
    // to know the manual Import button exists.
    private static readonly TimeSpan AutoImportThrottle = TimeSpan.FromHours(6);

    public RewardService(
        IApplicationDbContext db,
        ITwitchChannelPointsApi channelPoints,
        TimeProvider clock,
        ILogger<RewardService> logger
    )
    {
        _db = db;
        _channelPoints = channelPoints;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<RewardDetail>> CreateAsync(
        string broadcasterId,
        CreateRewardRequest request,
        bool pushToTwitch = true,
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

        Result pipelineOk = await _db.EnsurePipelineInChannelAsync(
            broadcaster,
            request.PipelineId,
            cancellationToken
        );
        if (pipelineOk.IsFailure)
            return pipelineOk.ToTyped<RewardDetail>();

        // Twitch has no server-side uniqueness on reward titles — creating a second "BSOD"-titled reward
        // would just duplicate it (and burn the streamer's channel-point economy). Check the LIVE Twitch
        // list (not just our local table, which can be stale/never-imported) before ever inserting. Fails
        // closed: if we can't verify, we don't blindly create either.
        Result<IReadOnlyList<TwitchCustomReward>> existingRewards =
            await _channelPoints.GetCustomRewardsAsync(
                broadcaster,
                onlyManageableRewards: false,
                ct: cancellationToken
            );
        if (existingRewards.IsFailure)
            return existingRewards.WithValue<RewardDetail>(default!);

        TwitchCustomReward? duplicate = existingRewards.Value.FirstOrDefault(r =>
            string.Equals(r.Title, request.Title, StringComparison.OrdinalIgnoreCase)
        );
        if (duplicate is not null)
            return Result.Failure<RewardDetail>(
                $"A reward titled '{duplicate.Title}' already exists on this channel (id {duplicate.Id}). "
                    + "Bind to the existing reward instead of creating a duplicate.",
                "ALREADY_EXISTS"
            );

        Reward reward;
        if (pushToTwitch)
        {
            // Twitch first: a reward that only ever exists in our own table is never redeemable, so nothing
            // here is real until Helix has created it. Fail closed on a Helix refusal — never persist a
            // definition that isn't actually live on the channel.
            Result<TwitchCustomReward> created = await _channelPoints.CreateCustomRewardAsync(
                broadcaster,
                new(
                    Title: request.Title,
                    Cost: request.Cost,
                    Prompt: request.Prompt,
                    IsEnabled: true,
                    BackgroundColor: request.BackgroundColor,
                    IsUserInputRequired: request.IsUserInputRequired,
                    IsMaxPerStreamEnabled: request.MaxPerStream.HasValue,
                    MaxPerStream: request.MaxPerStream,
                    IsMaxPerUserPerStreamEnabled: request.MaxPerUserPerStream.HasValue,
                    MaxPerUserPerStream: request.MaxPerUserPerStream,
                    IsGlobalCooldownEnabled: request.GlobalCooldownSeconds.HasValue,
                    GlobalCooldownSeconds: request.GlobalCooldownSeconds
                ),
                cancellationToken
            );
            if (created.IsFailure)
                return created.WithValue<RewardDetail>(default!);

            TwitchCustomReward tr = created.Value;

            // A bot-owned DEFINITION: manageable by construction (this client_id just created it on Twitch),
            // and the local row mirrors exactly what Helix confirmed — not the raw request — so a field
            // Twitch rejected, clamped, or defaulted never shows as if it were saved.
            reward = new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = broadcaster,
                Title = tr.Title,
                Cost = tr.Cost,
                Description = tr.Prompt,
                Response = request.Response,
                IsEnabled = tr.IsEnabled,
                IsManageable = true,
                IsUserInputRequired = tr.IsUserInputRequired,
                TwitchRewardId = tr.Id,
                IsPlatform = true,
                TimerDurationSeconds = NormalizeTimerDuration(request.TimerDurationSeconds),
                PipelineId = request.PipelineId,
            };
            ApplyTwitchConfirmedFields(reward, tr);
        }
        else
        {
            // Deliberately offline (bundle import, D2): a LOCAL, bot-manageable definition with no
            // TwitchRewardId yet — never blocked by Helix being down. The caller owns getting it onto
            // Twitch afterward.
            reward = new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = broadcaster,
                Title = request.Title,
                Cost = request.Cost,
                Description = request.Prompt,
                Response = request.Response,
                BackgroundColor = request.BackgroundColor,
                MaxPerStream = request.MaxPerStream,
                MaxPerUserPerStream = request.MaxPerUserPerStream,
                GlobalCooldownSeconds = request.GlobalCooldownSeconds,
                IsEnabled = true,
                IsManageable = true,
                IsUserInputRequired = request.IsUserInputRequired,
                TimerDurationSeconds = NormalizeTimerDuration(request.TimerDurationSeconds),
                PipelineId = request.PipelineId,
            };
        }

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

        // The Twitch-facing subset of the patch. Bot-local bindings (TimerDurationSeconds, PipelineId) are
        // always ours to change; the reward's own identity/economy fields belong to whoever owns it on Twitch.
        bool patchesTwitchFacing =
            request.Title is not null
            || request.Cost.HasValue
            || request.Prompt is not null
            || request.IsEnabled.HasValue
            || request.IsPaused.HasValue
            || request.IsUserInputRequired.HasValue
            || request.BackgroundColor is not null
            || request.MaxPerStream.HasValue
            || request.MaxPerUserPerStream.HasValue
            || request.GlobalCooldownSeconds.HasValue;

        // A reward that lives on Twitch but was created by another client_id is read-only to us (Twitch
        // refuses the PATCH) — fail closed instead of drifting the local copy away from the real reward.
        if (patchesTwitchFacing && reward.TwitchRewardId is not null && !reward.IsManageable)
            return Result.Failure<RewardDetail>(
                "This reward was created outside the bot and is read-only; convert it to bot-controlled first.",
                "FORBIDDEN"
            );

        if (request.PipelineId.HasValue)
        {
            Result pipelineOk = await _db.EnsurePipelineInChannelAsync(
                broadcaster,
                request.PipelineId.Value,
                cancellationToken
            );
            if (pipelineOk.IsFailure)
                return pipelineOk.ToTyped<RewardDetail>();
        }

        // Helix first, then the local copy — so a Twitch refusal never leaves the dashboard showing state
        // that is not live on Twitch. IsPaused syncs locally too, so the paused/resumed transition source
        // (RewardLifecycleHandler) and a dashboard patch can never disagree about the last-known state.
        TwitchCustomReward? confirmed = null;
        if (patchesTwitchFacing && reward is { IsManageable: true, TwitchRewardId: not null })
        {
            Result<TwitchCustomReward> pushed = await _channelPoints.UpdateCustomRewardAsync(
                broadcaster,
                reward.TwitchRewardId,
                new(
                    Title: request.Title,
                    Prompt: request.Prompt,
                    Cost: request.Cost,
                    BackgroundColor: request.BackgroundColor,
                    IsEnabled: request.IsEnabled,
                    IsUserInputRequired: request.IsUserInputRequired,
                    IsMaxPerStreamEnabled: LimitEnabled(request.MaxPerStream),
                    MaxPerStream: LimitValue(request.MaxPerStream),
                    IsMaxPerUserPerStreamEnabled: LimitEnabled(request.MaxPerUserPerStream),
                    MaxPerUserPerStream: LimitValue(request.MaxPerUserPerStream),
                    IsGlobalCooldownEnabled: LimitEnabled(request.GlobalCooldownSeconds),
                    GlobalCooldownSeconds: LimitValue(request.GlobalCooldownSeconds),
                    IsPaused: request.IsPaused
                ),
                cancellationToken
            );
            if (pushed.IsFailure)
                return Result.Failure<RewardDetail>(
                    pushed.ErrorMessage ?? "Twitch rejected the reward update.",
                    pushed.ErrorCode ?? "TWITCH_ERROR"
                );
            confirmed = pushed.Value;
        }

        // Cache exactly what Helix confirmed (never the raw request — Twitch can clamp/reject a value) so a
        // later read reflects the real Twitch state instead of always showing null for these four fields.
        if (confirmed is not null)
            ApplyTwitchConfirmedFields(reward, confirmed);

        if (request.Title is not null)
            reward.Title = request.Title;
        if (request.Cost.HasValue)
            reward.Cost = request.Cost.Value;
        if (request.Prompt is not null)
            reward.Description = request.Prompt;
        if (request.Response is not null)
            reward.Response = request.Response;
        if (request.IsEnabled.HasValue)
            reward.IsEnabled = request.IsEnabled.Value;
        if (request.IsPaused.HasValue)
            reward.IsPaused = request.IsPaused.Value;
        if (request.IsUserInputRequired.HasValue)
            reward.IsUserInputRequired = request.IsUserInputRequired.Value;
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

        // A bot-owned reward is deleted on Twitch too — otherwise it lives on there, still redeemable, while
        // the soft-deleted row keeps its Twitch id and the next sync trips the (BroadcasterId, TwitchRewardId)
        // unique index (live 2026-09-29). "Already gone on Twitch" is the outcome we want, not a failure.
        if (reward is { IsManageable: true, TwitchRewardId: not null })
        {
            Result removed = await _channelPoints.DeleteCustomRewardAsync(
                broadcaster,
                reward.TwitchRewardId,
                cancellationToken
            );
            if (removed.IsFailure && removed.ErrorCode != TwitchErrorCodes.NotFound)
                return removed;

            reward.TwitchRewardId = null;
        }

        _db.Rewards.Remove(reward);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// Counts what breaks if this reward goes. <c>Redemption</c> and <c>RedemptionTimer</c> both carry
    /// Twitch's reward UUID, not our row id, so the count keys on <see cref="Reward.TwitchRewardId"/>; a
    /// reward that was never synced to Twitch cannot be referenced at all, which is a verified zero.
    /// </summary>
    public async Task<Result<BlastRadiusDto>> GetDeleteBlastRadiusAsync(
        string broadcasterId,
        string rewardId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result<BlastRadiusDto>.Failure(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        if (!Guid.TryParse(rewardId, out Guid guid))
            return Result<BlastRadiusDto>.Failure(
                $"Invalid reward ID '{rewardId}'.",
                "VALIDATION_FAILED"
            );

        Reward? reward = await _db.Rewards.FirstOrDefaultAsync(
            r => r.Id == guid && r.BroadcasterId == broadcaster,
            cancellationToken
        );
        if (reward is null)
            return Result<BlastRadiusDto>.Failure(
                $"Reward '{rewardId}' was not found.",
                "NOT_FOUND"
            );

        List<BlastRadiusCategoryDto> categories = [];
        if (!string.IsNullOrWhiteSpace(reward.TwitchRewardId))
        {
            string twitchRewardId = reward.TwitchRewardId;

            List<string> redeemers = await _db
                .Redemptions.Where(r =>
                    r.BroadcasterId == broadcaster && r.RewardId == twitchRewardId
                )
                .OrderBy(r => r.RedeemedAt)
                .Select(r => r.UserDisplayName)
                .Take(SampleSize)
                .ToListAsync(cancellationToken);
            int redemptionCount = await _db.Redemptions.CountAsync(
                r => r.BroadcasterId == broadcaster && r.RewardId == twitchRewardId,
                cancellationToken
            );
            if (redemptionCount > 0)
                categories.Add(
                    new BlastRadiusCategoryDto(
                        BlastRadiusCategoryKeys.Redemptions,
                        redemptionCount,
                        redeemers
                    )
                );

            int timerCount = await _db.RedemptionTimers.CountAsync(
                t => t.BroadcasterId == broadcaster && t.RewardId == twitchRewardId,
                cancellationToken
            );
            if (timerCount > 0)
                categories.Add(
                    new BlastRadiusCategoryDto(
                        BlastRadiusCategoryKeys.RedemptionTimers,
                        timerCount,
                        []
                    )
                );
        }

        // Every referencing table carries a real FK, so this total is exhaustive — never a floor.
        return Result<BlastRadiusDto>.Success(new BlastRadiusDto(categories, IsMinimum: false));
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

        await EnsureRecentlyImportedAsync(broadcaster, cancellationToken);

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
        List<RewardDetail> items = [.. rewards.Select(ToDetail)];

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
            .GroupJoin(
                _db.Users,
                r => r.UserId,
                u => u.TwitchUserId,
                (r, users) => new { Redemption = r, User = users.FirstOrDefault() }
            )
            .Select(x => new RedemptionListItem(
                x.Redemption.RedemptionId,
                x.Redemption.RewardId,
                x.Redemption.RewardTitle,
                x.Redemption.UserId,
                x.Redemption.UserDisplayName,
                x.User != null ? x.User.ProfileImageUrl : null,
                x.Redemption.Cost,
                x.Redemption.UserInput,
                x.Redemption.Status,
                x.Redemption.RedeemedAt
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
        string? rewardId = null,
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
        // A pipeline run triggered by the redemption can beat the projection: the caller's reward id then
        // addresses Helix directly.
        string? helixRewardId = row?.RewardId ?? rewardId;
        if (string.IsNullOrEmpty(helixRewardId))
            return Result.Failure($"Redemption '{redemptionId}' was not found.", "NOT_FOUND");

        Result<IReadOnlyList<TwitchCustomRewardRedemption>> helix =
            await _channelPoints.UpdateRedemptionStatusAsync(
                broadcaster,
                helixRewardId,
                [redemptionId],
                new(twitchStatus),
                cancellationToken
            );
        if (helix.IsFailure)
            return Result.Failure(
                helix.ErrorMessage ?? "Twitch rejected the redemption update.",
                helix.ErrorCode ?? "TWITCH_ERROR"
            );

        // Row not folded yet: the EventSub redemption.update folds the status through the projection later.
        if (row is null)
            return Result.Success();

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
        int syncedCount = 0;
        if (twitchRewards.Count == 0)
        {
            _logger.LogInformation(
                "Reward sync: Twitch returned no bot-managed rewards for broadcaster {BroadcasterId} "
                    + "(streamer-created rewards are unmanaged and surface at redemption time, not via sync)",
                broadcasterId
            );
        }
        else
        {
            // Sync read `only_manageable_rewards=true`, so every reward it received is one THIS client can
            // manage — the whole returned set IS the manageable id set.
            HashSet<string> manageableRewardIds = twitchRewards
                .Select(r => r.Id)
                .ToHashSet(StringComparer.Ordinal);

            Result<int> upserted = await UpsertTwitchRewardsAsync(
                broadcaster,
                twitchRewards,
                manageableRewardIds,
                cancellationToken
            );
            if (upserted.IsFailure)
                return upserted;
            syncedCount = upserted.Value;
        }

        // The other direction: any bot-manageable LOCAL reward Twitch has never seen (no TwitchRewardId) —
        // e.g. one a bundle import created deliberately local-only (D2) — is pushed to Helix now, best-effort.
        // This is what makes "sync pushes it to Twitch later" actually true instead of a promise nothing
        // fulfills; a refusal on one reward is logged and skipped, never fails the whole sync.
        int pushedCount = await PushUnsyncedLocalRewardsAsync(broadcaster, cancellationToken);

        _logger.LogInformation(
            "Synced {PulledCount} rewards from Twitch and pushed {PushedCount} local-only rewards to Twitch "
                + "for broadcaster {BroadcasterId}",
            syncedCount,
            pushedCount,
            broadcasterId
        );
        return Result.Success();
    }

    /// <summary>
    /// The onboarding half of the ownership-migration flow (rewards.md): a streamer's pre-existing
    /// Twitch-dashboard-created rewards need to become visible (read-only, "Take control") without them ever
    /// finding the manual Import button. Runs <see cref="ImportFromTwitchAsync"/> in the background of an
    /// ordinary <see cref="ListAsync"/> call, throttled by <see cref="Channel.RewardsSyncedAt"/> so it costs a
    /// Helix round trip only once per <see cref="AutoImportThrottle"/> window per channel — never on every page
    /// load. A failed import (dead token, no Twitch connection, transient error) is logged and swallowed: it
    /// must never break the reward list itself, and the throttle stamp still advances either way so a
    /// struggling channel is not retried on every single visit.
    /// </summary>
    private async Task EnsureRecentlyImportedAsync(
        Guid broadcaster,
        CancellationToken cancellationToken
    )
    {
        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.Id == broadcaster,
            cancellationToken
        );
        if (channel is null)
            return;

        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;
        if (
            channel.RewardsSyncedAt is not null
            && nowUtc - channel.RewardsSyncedAt.Value < AutoImportThrottle
        )
            return;

        Result importResult = await ImportFromTwitchAsync(
            broadcaster.ToString(),
            cancellationToken
        );
        if (importResult.IsFailure)
            _logger.LogDebug(
                "Reward auto-import (background sync from ListAsync) failed for {BroadcasterId}: {Error}",
                broadcaster,
                importResult.ErrorMessage
            );

        channel.RewardsSyncedAt = nowUtc;
        await _db.SaveChangesAsync(cancellationToken);
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
            await FinishParkedTakeOversAsync(broadcaster, twitchRewards, cancellationToken);
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

        Result<int> imported = await UpsertTwitchRewardsAsync(
            broadcaster,
            twitchRewards,
            manageableRewardIds,
            cancellationToken
        );
        if (imported.IsFailure)
            return imported;

        await FinishParkedTakeOversAsync(broadcaster, twitchRewards, cancellationToken);

        _logger.LogInformation(
            "Imported {Count} rewards (managed + external) for broadcaster {BroadcasterId}",
            imported.Value,
            broadcasterId
        );
        return Result.Success();
    }

    /// <summary>
    /// Completes every parked take-over whose original no longer blocks its title on Twitch — deleted there
    /// (its id is gone from the full list) or renamed (its id now carries another title). The streamer did
    /// the one step only they can do; finishing is ours, with no second click. A take-over Twitch still
    /// refuses simply stays parked for the next import.
    /// </summary>
    private async Task FinishParkedTakeOversAsync(
        Guid broadcaster,
        IReadOnlyList<TwitchCustomReward> fullTwitchList,
        CancellationToken cancellationToken
    )
    {
        Dictionary<string, string> liveTitleById = fullTwitchList.ToDictionary(
            r => r.Id,
            r => r.Title,
            StringComparer.Ordinal
        );
        List<Reward> parked = await _db
            .Rewards.Where(r =>
                r.BroadcasterId == broadcaster
                && r.PendingMigrationRequestedAt != null
                && !r.IsManageable
            )
            .ToListAsync(cancellationToken);

        foreach (Reward reward in parked)
        {
            bool originalStillBlocks =
                reward.TwitchRewardId is not null
                && liveTitleById.TryGetValue(reward.TwitchRewardId, out string? liveTitle)
                && string.Equals(liveTitle, reward.Title, StringComparison.OrdinalIgnoreCase);
            if (originalStillBlocks)
                continue;

            Result<RewardDetail> finished = await RecreateUnderBotAsync(
                broadcaster.ToString(),
                reward.Id.ToString(),
                cancellationToken
            );
            if (finished.IsFailure)
                _logger.LogInformation(
                    "Reward import: parked take-over of '{Title}' for {BroadcasterId} is not finished yet: {Error}",
                    reward.Title,
                    broadcaster,
                    finished.ErrorMessage
                );
        }
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

        // Twitch's Create Custom Reward requires the title be unique amongst ALL of the broadcaster's custom
        // rewards — regardless of enabled/paused state — so creating a copy of `external` under the title it
        // already occupies always 400s. And an unmanaged external reward can't be renamed, disabled, or deleted
        // through this client_id (only the client_id that created it can), so "take control" can never succeed
        // on Twitch's side while that title is still taken. Rather than a dead-end failure, we PARK the request:
        // `external` already carries every field the recreate needs, so nothing is lost — mark it pending and
        // tell the operator what to do on Twitch. The dashboard swaps the button to "Finalize migration"; the
        // NEXT click re-enters this same method, re-checks Twitch, and — once the title is free — completes the
        // recreate below and clears the pending marker.
        //
        // This has to re-check the LIVE Twitch reward list, not the local table: `external` itself is the only
        // local row that ever carried this title, so a local-only check always finds nothing taken and sails
        // straight into CreateCustomReward, which Twitch then 400s with the raw DUPLICATE_REWARD error (the
        // operator-parking path never engages). Same reasoning as CreateAsync's pre-check above.
        //
        // This live-list read is a best-effort short-circuit, not a guarantee: verified in production
        // (2026-09-10) that Twitch's own create validation can still reject a title as a duplicate even when
        // this very read, moments earlier, showed no conflicting entry — Twitch appears to hold a just-freed
        // title in reserve past what the list endpoint reflects. The failure branch below on the CREATE call
        // itself is the real backstop: it recognizes that same Twitch signal and parks exactly like this does,
        // so a miss here still ends in the guided flow instead of leaking a raw Twitch error code.
        Result<IReadOnlyList<TwitchCustomReward>> liveRewards =
            await _channelPoints.GetCustomRewardsAsync(
                broadcaster,
                onlyManageableRewards: false,
                ct: cancellationToken
            );
        if (liveRewards.IsFailure)
            return liveRewards.WithValue<RewardDetail>(default!);

        // Exclude `external`'s own live entry — it hasn't been deleted yet by definition (we're trying to
        // take it over), so it always matches its own title; the conflict we care about is any OTHER entry
        // still sitting on that name.
        bool titleStillTaken = liveRewards.Value.Any(r =>
            r.Id != external.TwitchRewardId
            && string.Equals(r.Title, external.Title, StringComparison.OrdinalIgnoreCase)
        );
        if (titleStillTaken)
            return await ParkPendingMigrationAsync(external, cancellationToken);

        // The title is free — either this is a first attempt, or the operator cleared the conflict on Twitch
        // and is finalizing a previously-parked migration. Either way, proceed and drop any pending marker.
        external.PendingMigrationRequestedAt = null;

        // We cannot take over the original (another client_id owns it), so we recreate an equivalent reward
        // under the bot's client. The new reward gets its own Twitch id and IS manageable.
        Result<TwitchCustomReward> created = await _channelPoints.CreateCustomRewardAsync(
            broadcaster,
            new(
                Title: external.Title,
                Cost: external.Cost ?? 0,
                Prompt: external.Description,
                IsEnabled: external.IsEnabled
            ),
            cancellationToken
        );
        if (created.IsFailure)
        {
            // Twitch's create validation caught a duplicate title the live-list pre-check above missed (see the
            // comment on that check) — trust Twitch's own signal and park exactly the same way, rather than
            // surfacing its raw error code as an opaque, unactionable failure.
            if (IsDuplicateRewardSignal(created.ErrorDetail))
                return await ParkPendingMigrationAsync(external, cancellationToken);

            return created.WithValue<RewardDetail>(default!);
        }

        // The local row IS the reward: re-point it at the bot-owned copy so its pipeline, response, limits and
        // cooldowns carry over. Inserting a second row (the old behavior) listed the reward twice with an
        // unconfigured copy, which streamers then deleted — orphaning the bot reward on Twitch.
        TwitchCustomReward tr = created.Value;
        string? originalTwitchRewardId = external.TwitchRewardId;
        external.TwitchRewardId = tr.Id;
        external.Title = tr.Title;
        external.Description = tr.Prompt;
        external.Cost = tr.Cost;
        external.IsEnabled = tr.IsEnabled;
        external.IsPaused = tr.IsPaused;
        external.IsPlatform = true;
        external.IsManageable = true;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Took control of reward '{Title}' ({OriginalTwitchRewardId}) as bot reward {TwitchRewardId} for {BroadcasterId}",
            external.Title,
            originalTwitchRewardId,
            tr.Id,
            broadcasterId
        );
        return Result.Success(ToDetail(external));
    }

    /// <summary>
    /// Marks <paramref name="external"/> as a parked migration and returns the actionable
    /// <c>MIGRATION_PENDING_EXTERNAL_REMOVAL</c> failure — the shared outcome for BOTH duplicate-title paths in
    /// <see cref="RecreateUnderBotAsync"/> (the live-list pre-check and the create-call backstop): nothing
    /// configured on <paramref name="external"/> is lost, and the operator gets a concrete next step instead of
    /// a dead-end failure.
    /// </summary>
    private async Task<Result<RewardDetail>> ParkPendingMigrationAsync(
        Reward external,
        CancellationToken cancellationToken
    )
    {
        if (external.PendingMigrationRequestedAt is null)
        {
            external.PendingMigrationRequestedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Result.Failure<RewardDetail>(
            $"Twitch won't allow a second reward titled \"{external.Title}\" — and the bot can't rename or "
                + "remove the original since it wasn't created by this client. Rename or delete "
                + $"\"{external.Title}\" in your Twitch Creator Dashboard, then click Finalize migration to "
                + "finish taking control — nothing you've configured here will be lost while you wait.",
            "MIGRATION_PENDING_EXTERNAL_REMOVAL"
        );
    }

    /// <summary>
    /// Recognizes Twitch's own Create Custom Reward duplicate-title rejection from the raw Helix error detail
    /// (<see cref="Result{T}.ErrorDetail"/>) — confirmed against production (2026-09-10) to be the literal
    /// message <c>CREATE_CUSTOM_REWARD_DUPLICATE_REWARD</c>. Matched by substring, case-insensitively, so a
    /// minor wording change on Twitch's side degrades to "no match" (falls through to the generic Twitch-error
    /// path) rather than throwing.
    /// </summary>
    private static bool IsDuplicateRewardSignal(string? twitchErrorDetail) =>
        twitchErrorDetail is not null
        && twitchErrorDetail.Contains("DUPLICATE_REWARD", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Upserts a set of Twitch rewards into the local table, matching first by Twitch id, then by title
    /// (linking a locally-created reward), else creating a new row. A reward is manageable iff its Twitch id is
    /// in <paramref name="manageableRewardIds"/> — the id set Twitch returns for
    /// <c>only_manageable_rewards=true</c>, i.e. the rewards THIS client_id created. That drives both the
    /// persisted <see cref="Reward.IsManageable"/> (external rewards recorded read-only) and the platform flag
    /// on a newly-created row. Twitch's reward payload carries no manageability field, so it is never inferred
    /// from the wire. Shared by sync (managed set only) and import (full set). Returns the number upserted.
    /// </summary>
    private async Task<Result<int>> UpsertTwitchRewardsAsync(
        Guid broadcaster,
        IReadOnlyList<TwitchCustomReward> twitchRewards,
        IReadOnlySet<string> manageableRewardIds,
        CancellationToken cancellationToken
    )
    {
        // Soft-deleted rows included: the (BroadcasterId, TwitchRewardId) unique index counts them, so a sync
        // that cannot see them would link or insert an id a deleted row still holds and throw (live 2026-09-29).
        List<Reward> existing = await _db
            .Rewards.IgnoreQueryFilters()
            .Where(r => r.BroadcasterId == broadcaster)
            .ToListAsync(cancellationToken);

        Dictionary<string, Reward> existingByTwitchId = existing
            .Where(r => r.TwitchRewardId != null)
            .ToDictionary(r => r.TwitchRewardId!);

        // Title-match links a live local reward that has no Twitch id yet (a local-only create, or a parked
        // take-over whose original is gone). Titles are not unique locally, so keep the first candidate.
        Dictionary<string, Reward> existingByTitle = existing
            .Where(r => r.TwitchRewardId is null && r.DeletedAt is null)
            .GroupBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        List<Reward> touched = [];
        int upsertedCount = 0;
        foreach (TwitchCustomReward tr in twitchRewards)
        {
            bool manageable = manageableRewardIds.Contains(tr.Id);
            if (existingByTwitchId.TryGetValue(tr.Id, out Reward? reward))
            {
                // Deleted here but still on Twitch: never resurrect it and never hand its id to another row.
                if (reward.DeletedAt is not null)
                {
                    _logger.LogInformation(
                        "Reward sync: Twitch reward {TwitchRewardId} ('{Title}') belongs to a reward deleted locally for {BroadcasterId}; left as is.",
                        tr.Id,
                        tr.Title,
                        broadcaster
                    );
                    continue;
                }

                // A parked take-over keeps the title it is waiting to claim; a renamed original must not
                // drag the row's title along with it.
                if (reward.PendingMigrationRequestedAt is null)
                    reward.Title = tr.Title;
                ApplyTwitchState(reward, tr, manageable);
                touched.Add(reward);
                upsertedCount++;
            }
            else if (existingByTitle.Remove(tr.Title, out Reward? rewardByTitle))
            {
                rewardByTitle.TwitchRewardId = tr.Id;
                ApplyTwitchState(rewardByTitle, tr, manageable);
                touched.Add(rewardByTitle);
                upsertedCount++;
            }
            else
            {
                Reward created = new()
                {
                    Id = Guid.NewGuid(),
                    BroadcasterId = broadcaster,
                    Title = tr.Title,
                    TwitchRewardId = tr.Id,
                    Cost = tr.Cost,
                    IsEnabled = tr.IsEnabled,
                    IsPaused = tr.IsPaused,
                    Description = tr.Prompt,
                    IsPlatform = manageable,
                    IsManageable = manageable,
                    IsUserInputRequired = tr.IsUserInputRequired,
                };
                _db.Rewards.Add(created);
                touched.Add(created);
                upsertedCount++;
            }
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Release every row this sync touched: a failed save must not stay tracked and poison the caller's
            // next SaveChanges (a script run shares this context — the whole run died after one failed sync).
            foreach (Reward row in touched)
                _db.Entry(row).State = EntityState.Detached;
            _logger.LogWarning(
                ex,
                "Reward sync: saving Twitch rewards failed for {BroadcasterId}; nothing was changed.",
                broadcaster
            );
            return Result.Failure<int>(
                "Saving the rewards read from Twitch failed; nothing was changed.",
                "REWARD_SYNC_CONFLICT"
            );
        }

        return Result.Success(upsertedCount);
    }

    /// <summary>Copies Twitch's live state onto a local row. A reward the bot owns is no longer a pending
    /// take-over — that is exactly what taking control produces.</summary>
    private static void ApplyTwitchState(Reward reward, TwitchCustomReward tr, bool manageable)
    {
        reward.Cost = tr.Cost;
        reward.IsEnabled = tr.IsEnabled;
        reward.IsPaused = tr.IsPaused;
        reward.Description = tr.Prompt;
        reward.IsManageable = manageable;
        reward.IsUserInputRequired = tr.IsUserInputRequired;
        if (manageable)
        {
            reward.IsPlatform = true;
            reward.PendingMigrationRequestedAt = null;
        }
    }

    /// <summary>
    /// Pushes every bot-manageable local reward Twitch has never seen (no <see cref="Reward.TwitchRewardId"/>)
    /// to Helix — the deferred half of a reward created with <c>pushToTwitch: false</c> (bundle import, D2).
    /// Best-effort per reward: a Helix refusal is logged and that one reward is left local-only for the next
    /// sync to retry, never failing the rest of the batch. Caches exactly what Twitch confirmed, same as
    /// <see cref="CreateAsync"/>'s Helix-first path. Returns the number actually pushed.
    /// </summary>
    private async Task<int> PushUnsyncedLocalRewardsAsync(
        Guid broadcaster,
        CancellationToken cancellationToken
    )
    {
        List<Reward> unsynced = await _db
            .Rewards.Where(r =>
                r.BroadcasterId == broadcaster && r.TwitchRewardId == null && r.IsManageable
            )
            .ToListAsync(cancellationToken);
        if (unsynced.Count == 0)
            return 0;

        int pushedCount = 0;
        foreach (Reward reward in unsynced)
        {
            Result<TwitchCustomReward> created = await _channelPoints.CreateCustomRewardAsync(
                broadcaster,
                new(
                    Title: reward.Title,
                    Cost: reward.Cost ?? 0,
                    Prompt: reward.Description,
                    IsEnabled: reward.IsEnabled,
                    BackgroundColor: reward.BackgroundColor,
                    IsUserInputRequired: reward.IsUserInputRequired,
                    IsMaxPerStreamEnabled: reward.MaxPerStream.HasValue,
                    MaxPerStream: reward.MaxPerStream,
                    IsMaxPerUserPerStreamEnabled: reward.MaxPerUserPerStream.HasValue,
                    MaxPerUserPerStream: reward.MaxPerUserPerStream,
                    IsGlobalCooldownEnabled: reward.GlobalCooldownSeconds.HasValue,
                    GlobalCooldownSeconds: reward.GlobalCooldownSeconds
                ),
                cancellationToken
            );
            if (created.IsFailure)
            {
                _logger.LogWarning(
                    "Reward sync: could not push local reward '{Title}' ({RewardId}) to Twitch for "
                        + "broadcaster {BroadcasterId}: {Error} ({Code})",
                    reward.Title,
                    reward.Id,
                    broadcaster,
                    created.ErrorMessage,
                    created.ErrorCode
                );
                continue;
            }

            TwitchCustomReward tr = created.Value;
            reward.TwitchRewardId = tr.Id;
            reward.Title = tr.Title;
            reward.Cost = tr.Cost;
            reward.IsEnabled = tr.IsEnabled;
            ApplyTwitchConfirmedFields(reward, tr);
            pushedCount++;
        }

        if (pushedCount > 0)
            await _db.SaveChangesAsync(cancellationToken);

        return pushedCount;
    }

    // A limit in an update patch: absent (null) leaves it as it is on Twitch (the field is left out of the
    // Helix body); 0 or less turns it off; a positive value turns it on with that value.
    private static bool? LimitEnabled(int? limit) => limit is { } value ? value > 0 : null;

    private static int? LimitValue(int? limit) => limit is > 0 ? limit : null;

    /// <summary>
    /// Applies the four reward fields Twitch itself decides on a create/update push — it can clamp, reject,
    /// or default any of them — so a later read reflects what's actually live instead of the raw request.
    /// Never applied from anything but a Helix-confirmed <see cref="TwitchCustomReward"/>. Shared by every
    /// path that just pushed to Helix: create, update, and the deferred local-to-Twitch sync push.
    /// </summary>
    private static void ApplyTwitchConfirmedFields(Reward reward, TwitchCustomReward confirmed)
    {
        reward.BackgroundColor = confirmed.BackgroundColor;
        reward.MaxPerStream = confirmed.MaxPerStreamSetting.IsEnabled
            ? confirmed.MaxPerStreamSetting.MaxPerStream
            : null;
        reward.MaxPerUserPerStream = confirmed.MaxPerUserPerStreamSetting.IsEnabled
            ? confirmed.MaxPerUserPerStreamSetting.MaxPerUserPerStream
            : null;
        reward.GlobalCooldownSeconds = confirmed.GlobalCooldownSetting.IsEnabled
            ? confirmed.GlobalCooldownSetting.GlobalCooldownSeconds
            : null;
    }

    /// <summary>Clamps a requested countdown to sane bounds: 0/negative clears it; the ceiling is 24h.</summary>
    private static int? NormalizeTimerDuration(int? requested) =>
        requested is { } seconds and > 0 ? Math.Min(seconds, 86_400) : null;

    private static RewardDetail ToDetail(Reward r) =>
        new(
            r.Id.ToString(),
            r.Title,
            r.Description,
            r.Response,
            r.Cost ?? 0,
            r.IsEnabled,
            r.IsManageable,
            r.IsUserInputRequired,
            r.IsPaused,
            r.PendingMigrationRequestedAt.HasValue,
            r.BackgroundColor,
            null,
            r.MaxPerStream,
            r.MaxPerUserPerStream,
            r.GlobalCooldownSeconds,
            r.TimerDurationSeconds,
            r.PipelineId,
            r.CreatedAt,
            r.UpdatedAt
        );
}
