// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY; See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Community.EventHandlers;

/// <summary>
/// Onboarding seed job (Community / standing domain): when a channel finishes onboarding, pull the channel's
/// VIP list from Helix and upsert each viewer's community standing to <c>CommunityStanding.Vip</c>. Requires
/// <c>channel:read:vips</c>; gracefully logs a warning and exits when the scope is absent. Creates User rows
/// via get-or-create for any VIP not yet in the local database. Independently resilient — caught + logged,
/// never propagated, idempotent and safe to re-run.
/// </summary>
public sealed class VipStandingSeedOnOnboardingHandler(
    ICommunityStandingService standings,
    IUserService users,
    ITwitchModeratorsApi moderators,
    ILogger<VipStandingSeedOnOnboardingHandler> logger
) : IEventHandler<ChannelOnboardedEvent>
{
    public async Task HandleAsync(ChannelOnboardedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        logger.LogInformation(
            "Onboarding seed (VIP standing): fetching VIP list from Twitch for {BroadcasterId} ({Name})",
            @event.BroadcasterId,
            @event.Name
        );

        try
        {
            Result<TwitchPage<TwitchVip>> result = await moderators.GetVipsAsync(
                @event.BroadcasterId,
                new TwitchPageRequest(),
                ct
            );

            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Onboarding seed (VIP standing): Helix VIP call failed for {BroadcasterId}: {Error} ({Code}) — VIP standings will be sourced from EventSub badges as chatters appear",
                    @event.BroadcasterId,
                    result.ErrorMessage,
                    result.ErrorCode
                );
                return;
            }

            int seeded = 0;
            foreach (TwitchVip vip in result.Value.Items)
            {
                Result<Application.Identity.Dtos.UserDto> userResult = await users.GetOrCreateAsync(
                    vip.UserId,
                    vip.UserLogin,
                    vip.UserName ?? vip.UserLogin,
                    ct
                );
                if (userResult.IsFailure)
                {
                    logger.LogWarning(
                        "Onboarding seed (VIP standing): could not resolve user {TwitchUserId}: {Error}",
                        vip.UserId,
                        userResult.ErrorMessage
                    );
                    continue;
                }

                if (!Guid.TryParse(userResult.Value.Id, out Guid userId))
                    continue;

                await standings.UpsertStandingAsync(
                    @event.BroadcasterId,
                    userId,
                    CommunityStanding.Vip,
                    StandingSource.HelixSeed,
                    subTier: null,
                    ct
                );
                seeded++;
            }

            logger.LogInformation(
                "Onboarding seed (VIP standing): completed for {BroadcasterId} — {Count} VIP(s) seeded",
                @event.BroadcasterId,
                seeded
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Onboarding seed (VIP standing): failed for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
