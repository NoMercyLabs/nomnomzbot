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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Identity.EventHandlers;

/// <summary>
/// Onboarding seed job (Identity / bot domain): when a channel finishes onboarding, grants the shared platform
/// bot moderator status on the new channel via Helix Add Channel Moderator, so the SaaS shared-bot identity can
/// moderate (timeout/ban/delete) from the moment it joins. A no-op when no shared <see cref="BotAccount"/> is
/// registered yet — the self-host default, where the streamer's own account IS the bot identity until a
/// dedicated bot is connected. Requires <c>channel:manage:moderators</c>, scope-gated inside
/// <see cref="ITwitchModeratorsApi.AddModeratorAsync"/>; a missing scope or an already-moderator bot both
/// surface as an ordinary failure here, logged as a warning — idempotent and safe to re-run, since this
/// handler writes nothing locally (the Helix call is its only side effect). Independently resilient — caught +
/// logged, never propagated, so it cannot affect the other onboarding seed jobs.
/// </summary>
public sealed class BotJoinOnOnboardingHandler(
    IApplicationDbContext db,
    ITwitchModeratorsApi moderators,
    ILogger<BotJoinOnOnboardingHandler> logger
) : IEventHandler<ChannelOnboardedEvent>
{
    public async Task HandleAsync(ChannelOnboardedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        try
        {
            BotAccount? sharedBot = await db
                .BotAccounts.AsNoTracking()
                .FirstOrDefaultAsync(
                    b =>
                        b.IdentityType == AuthEnums.BotIdentityType.Shared
                        && b.IsActive
                        && b.DeletedAt == null,
                    ct
                );

            if (sharedBot is null)
            {
                logger.LogInformation(
                    "Onboarding seed (bot join): no shared platform bot registered — skipping mod-grant for {BroadcasterId}",
                    @event.BroadcasterId
                );
                return;
            }

            Result modResult = await moderators.AddModeratorAsync(
                @event.BroadcasterId,
                sharedBot.BotUserId,
                ct
            );

            if (modResult.IsSuccess)
                logger.LogInformation(
                    "Onboarding seed (bot join): granted moderator status to the shared bot {BotUsername} in {BroadcasterId}",
                    sharedBot.BotUsername,
                    @event.BroadcasterId
                );
            else
                logger.LogWarning(
                    "Onboarding seed (bot join): could not grant moderator status to the shared bot in {BroadcasterId}: {Error} ({Code})",
                    @event.BroadcasterId,
                    modResult.ErrorMessage,
                    modResult.ErrorCode
                );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Onboarding seed (bot join): failed for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
