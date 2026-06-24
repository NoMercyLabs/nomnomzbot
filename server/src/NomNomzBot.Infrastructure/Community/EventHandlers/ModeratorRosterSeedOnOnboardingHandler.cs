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
using NomNomzBot.Application.Community.Services;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Community.EventHandlers;

/// <summary>
/// Onboarding seed job (Community domain): when a channel finishes onboarding, pull its moderator roster from
/// Twitch into the local <c>ChannelModerators</c> table — the source the Community page's moderator tab and
/// moderator-count stat read. Independently resilient — caught + logged, never propagated, so it cannot affect
/// the other onboarding seed jobs. <see cref="ICommunityRosterService.SyncModeratorsFromTwitchAsync"/> is an
/// idempotent upsert, so this is safe to run on every onboarding + backfill.
/// </summary>
public sealed class ModeratorRosterSeedOnOnboardingHandler(
    ICommunityRosterService roster,
    ILogger<ModeratorRosterSeedOnOnboardingHandler> logger
) : IEventHandler<ChannelOnboardedEvent>
{
    public async Task HandleAsync(ChannelOnboardedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        logger.LogInformation(
            "Onboarding seed (community): syncing moderator roster from Twitch for {BroadcasterId} ({Name})",
            @event.BroadcasterId,
            @event.Name
        );

        try
        {
            Result<int> result = await roster.SyncModeratorsFromTwitchAsync(
                @event.BroadcasterId,
                ct
            );

            if (result.IsSuccess)
                logger.LogInformation(
                    "Onboarding seed (community): completed for {BroadcasterId} ({Created} moderator(s) added)",
                    @event.BroadcasterId,
                    result.Value
                );
            else
                logger.LogWarning(
                    "Onboarding seed (community): sync returned a failure for {BroadcasterId}: {Error} ({Code})",
                    @event.BroadcasterId,
                    result.ErrorMessage,
                    result.ErrorCode
                );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Onboarding seed (community): failed for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
