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
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Domain.Rewards.Entities;

namespace NomNomzBot.Infrastructure.Rewards;

/// <inheritdoc cref="IOfflineRewardService"/>
public sealed class OfflineRewardService(
    IApplicationDbContext db,
    ITwitchChannelPointsApi channelPoints,
    ILogger<OfflineRewardService> logger
) : IOfflineRewardService
{
    public async Task DisableForOfflineAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        bool keepActive = await db
            .Channels.Where(c => c.Id == broadcasterId)
            .Select(c => c.KeepActiveWhileOffline)
            .FirstOrDefaultAsync(cancellationToken);
        if (keepActive)
            return;

        List<Reward> rewards = await db
            .Rewards.Where(r =>
                r.BroadcasterId == broadcasterId
                && r.IsEnabled
                && r.IsManageable
                && r.TwitchRewardId != null
            )
            .ToListAsync(cancellationToken);

        foreach (Reward reward in rewards)
        {
            if (!await TrySetEnabledAtTwitchAsync(broadcasterId, reward, false, cancellationToken))
                continue;

            reward.IsEnabled = false;
            reward.DisabledForOffline = true;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task EnableMarkedAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<Reward> rewards = await db
            .Rewards.Where(r =>
                r.BroadcasterId == broadcasterId && r.DisabledForOffline && r.TwitchRewardId != null
            )
            .ToListAsync(cancellationToken);

        foreach (Reward reward in rewards)
        {
            if (!await TrySetEnabledAtTwitchAsync(broadcasterId, reward, true, cancellationToken))
                continue;

            reward.IsEnabled = true;
            reward.DisabledForOffline = false;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ApplyKeepActiveSettingAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        var state = await db
            .Channels.Where(c => c.Id == broadcasterId)
            .Select(c => new { c.IsLive, c.KeepActiveWhileOffline })
            .FirstOrDefaultAsync(cancellationToken);
        if (state is { IsLive: false, KeepActiveWhileOffline: true })
            await EnableMarkedAsync(broadcasterId, cancellationToken);
    }

    public Task ApplyLiveStateAsync(
        Guid broadcasterId,
        bool isLive,
        CancellationToken cancellationToken = default
    ) =>
        isLive
            ? EnableMarkedAsync(broadcasterId, cancellationToken)
            : DisableForOfflineAsync(broadcasterId, cancellationToken);

    // A refusal (a reward another app created, a revoked scope, Helix down) is logged and skipped: the row
    // keeps its old state, so the next edge retries it, and one bad reward never stops the rest.
    private async Task<bool> TrySetEnabledAtTwitchAsync(
        Guid broadcasterId,
        Reward reward,
        bool enabled,
        CancellationToken cancellationToken
    )
    {
        Result<TwitchCustomReward> pushed = await channelPoints.UpdateCustomRewardAsync(
            broadcasterId,
            reward.TwitchRewardId!,
            new(IsEnabled: enabled),
            cancellationToken
        );
        if (pushed.IsSuccess)
            return true;

        logger.LogWarning(
            "Reward {RewardId} of channel {BroadcasterId} was not set to enabled={Enabled} at Twitch: {Error}",
            reward.TwitchRewardId,
            broadcasterId,
            enabled,
            pushed.ErrorMessage
        );
        return false;
    }
}
