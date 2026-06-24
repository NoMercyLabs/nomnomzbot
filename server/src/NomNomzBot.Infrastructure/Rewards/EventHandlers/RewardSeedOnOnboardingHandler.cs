// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Rewards.EventHandlers;

/// <summary>
/// Onboarding seed job (Rewards domain): when a channel finishes onboarding, pull its existing channel-point
/// rewards from Twitch into the local Rewards table so the dashboard's Rewards page is populated from the
/// first load. Independently resilient — a failure here is caught and logged, never propagated, so it cannot
/// affect the other onboarding seed jobs. <see cref="IRewardService.SyncWithTwitchAsync"/> is an idempotent
/// upsert (matched by Twitch reward id / title), so this is safe to run on every onboarding + backfill.
/// </summary>
public sealed class RewardSeedOnOnboardingHandler(
    IRewardService rewards,
    ILogger<RewardSeedOnOnboardingHandler> logger
) : IEventHandler<ChannelOnboardedEvent>
{
    public async Task HandleAsync(ChannelOnboardedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        logger.LogInformation(
            "Onboarding seed (rewards): syncing channel-point rewards from Twitch for {BroadcasterId} ({Name})",
            @event.BroadcasterId,
            @event.Name
        );

        try
        {
            Result result = await rewards.SyncWithTwitchAsync(@event.BroadcasterId.ToString(), ct);

            if (result.IsSuccess)
                logger.LogInformation(
                    "Onboarding seed (rewards): completed for {BroadcasterId}",
                    @event.BroadcasterId
                );
            else
                logger.LogWarning(
                    "Onboarding seed (rewards): sync returned a failure for {BroadcasterId}: {Error} ({Code})",
                    @event.BroadcasterId,
                    result.ErrorMessage,
                    result.ErrorCode
                );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Onboarding seed (rewards): failed for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
