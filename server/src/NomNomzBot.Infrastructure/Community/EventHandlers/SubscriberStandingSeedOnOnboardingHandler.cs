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
/// Onboarding seed job (Community / standing domain): when a channel finishes onboarding, pull the subscriber
/// list from Helix (all pages) and upsert each subscriber's community standing to
/// <c>CommunityStanding.Subscriber</c> with their sub tier (1000 / 2000 / 3000). Requires
/// <c>channel:read:subscriptions</c>; gracefully logs a warning and exits when the scope is absent. Creates
/// User rows via get-or-create for any subscriber not yet in the local database. Independently resilient —
/// caught + logged, never propagated, idempotent and safe to re-run.
/// </summary>
public sealed class SubscriberStandingSeedOnOnboardingHandler(
    ICommunityStandingService standings,
    IUserService users,
    ITwitchSubscriptionsApi subscriptions,
    ILogger<SubscriberStandingSeedOnOnboardingHandler> logger
) : IEventHandler<ChannelOnboardedEvent>
{
    public async Task HandleAsync(ChannelOnboardedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        logger.LogInformation(
            "Onboarding seed (subscriber standing): fetching subscriber list from Twitch for {BroadcasterId} ({Name})",
            @event.BroadcasterId,
            @event.Name
        );

        try
        {
            int seeded = 0;
            string? cursor = null;

            do
            {
                TwitchPageRequest page = new() { After = cursor };
                Result<TwitchPage<TwitchBroadcasterSubscription>> result =
                    await subscriptions.GetBroadcasterSubscriptionsAsync(
                        @event.BroadcasterId,
                        filterTwitchUserIds: null,
                        page,
                        ct
                    );

                if (result.IsFailure)
                {
                    // Log once and exit — scope may be missing, no retrying page by page.
                    logger.LogWarning(
                        "Onboarding seed (subscriber standing): Helix subscriptions call failed for {BroadcasterId}: {Error} ({Code}) — subscriber standings will be sourced from EventSub badges as chatters appear",
                        @event.BroadcasterId,
                        result.ErrorMessage,
                        result.ErrorCode
                    );
                    return;
                }

                foreach (TwitchBroadcasterSubscription sub in result.Value.Items)
                {
                    // Skip gift senders; they appear in the list but are not themselves subscribers.
                    if (string.IsNullOrEmpty(sub.UserId))
                        continue;

                    Result<Application.Identity.Dtos.UserDto> userResult =
                        await users.GetOrCreateAsync(
                            sub.UserId,
                            sub.UserLogin,
                            sub.UserName,
                            cancellationToken: ct
                        );
                    if (userResult.IsFailure)
                    {
                        logger.LogWarning(
                            "Onboarding seed (subscriber standing): could not resolve user {TwitchUserId}: {Error}",
                            sub.UserId,
                            userResult.ErrorMessage
                        );
                        continue;
                    }

                    if (!Guid.TryParse(userResult.Value.Id, out Guid userId))
                        continue;

                    await standings.UpsertStandingAsync(
                        @event.BroadcasterId,
                        userId,
                        CommunityStanding.Subscriber,
                        StandingSource.HelixSeed,
                        subTier: sub.Tier,
                        ct
                    );
                    seeded++;
                }

                cursor = result.Value.NextCursor;
            } while (!string.IsNullOrEmpty(cursor));

            logger.LogInformation(
                "Onboarding seed (subscriber standing): completed for {BroadcasterId} — {Count} subscriber(s) seeded",
                @event.BroadcasterId,
                seeded
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Onboarding seed (subscriber standing): failed for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
