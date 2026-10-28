// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Moderator self-heal for the shared platform bot. Mirrors the onboarding grant: the same configured-bot gate,
/// the same <see cref="OutboundSanction.PlatformConfiguration"/> basis, the broadcaster's own token. A refused
/// grant for a missing scope becomes a recorded scope gap, which the streamer sees in the action-required inbox.
/// </summary>
public sealed class BotModeratorGrantService(
    IApplicationDbContext db,
    ITwitchModeratorsApi moderators,
    IScopeNotificationService scopeNotifications,
    IConfiguration configuration,
    IOutboundSanctionAccessor sanctions,
    BotModeratorGrantBackoff backoff,
    ILogger<BotModeratorGrantService> logger
) : IBotModeratorGrantService
{
    public async Task<Result<bool>> TryGrantAsync(
        Guid broadcasterId,
        ChannelTwitchBot bot,
        CancellationToken cancellationToken = default
    )
    {
        BotAccount? sharedBot = await db
            .BotAccounts.AsNoTracking()
            .FirstOrDefaultAsync(
                b =>
                    b.IdentityType == AuthEnums.BotIdentityType.Shared
                    && b.IsActive
                    && b.DeletedAt == null
                    && b.BotUserId == bot.TwitchUserId,
                cancellationToken
            );
        if (
            sharedBot is null
            || !SaasBotPin.IsConfigured(configuration, sharedBot.BotUserId, sharedBot.BotUsername)
        )
            return Result.Success(false);

        if (!backoff.TryEnter(broadcasterId))
            return Result.Success(false);

        using IDisposable sanction = sanctions.Begin(
            OutboundSanction.PlatformConfiguration(
                $"saas_bot_moderator_regrant:{sharedBot.BotUsername}"
            )
        );
        Result granted = await moderators.AddModeratorAsync(
            broadcasterId,
            sharedBot.BotUserId,
            cancellationToken
        );
        if (granted.IsSuccess)
        {
            logger.LogInformation(
                "Bot moderator self-heal: granted moderator to {BotUsername} in {BroadcasterId}",
                sharedBot.BotUsername,
                broadcasterId
            );
            return Result.Success(true);
        }

        if (granted.ErrorCode == TwitchErrorCodes.MissingScope)
            await scopeNotifications.RecordMissingScopeAsync(
                broadcasterId,
                TwitchScopes.ChannelManageModerators,
                null,
                cancellationToken
            );

        logger.LogWarning(
            "Bot moderator self-heal: could not grant moderator to {BotUsername} in {BroadcasterId}: {Error} ({Code})",
            sharedBot.BotUsername,
            broadcasterId,
            granted.ErrorMessage,
            granted.ErrorCode
        );
        return Result.Success(false);
    }
}
