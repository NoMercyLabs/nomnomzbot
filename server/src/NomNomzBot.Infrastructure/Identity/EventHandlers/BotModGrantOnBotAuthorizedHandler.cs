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
/// The backfill twin of <see cref="BotJoinOnOnboardingHandler"/>: when the SHARED platform bot connects
/// (<see cref="BotAccountAuthorizedEvent"/>), grants it moderator status on every channel that onboarded
/// BEFORE the bot existed — the onboarding-time grant never ran for those. Being a moderator satisfies the
/// broadcaster-side half of the app-token (badge) chat send on channels whose broadcaster never granted
/// <c>channel:bot</c>. Per-channel best-effort: requires that channel's <c>channel:manage:moderators</c>
/// (scope-gated inside <see cref="ITwitchModeratorsApi.AddModeratorAsync"/>); a missing scope or an
/// already-moderator bot logs a warning and the sweep continues. Writes nothing locally — the Helix calls
/// are the only side effect, so re-running (bot re-auth) is safe.
/// </summary>
public sealed class BotModGrantOnBotAuthorizedHandler(
    IApplicationDbContext db,
    ITwitchModeratorsApi moderators,
    ILogger<BotModGrantOnBotAuthorizedHandler> logger
) : IEventHandler<BotAccountAuthorizedEvent>
{
    public async Task HandleAsync(BotAccountAuthorizedEvent @event, CancellationToken ct = default)
    {
        if (@event.IdentityType != AuthEnums.BotIdentityType.Shared)
            return;

        try
        {
            BotAccount? bot = await db
                .BotAccounts.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == @event.BotAccountId && b.DeletedAt == null, ct);
            if (bot is null || string.IsNullOrEmpty(bot.BotUserId))
                return;

            List<Guid> channelIds = await db
                .Channels.Where(c => c.Enabled && c.IsOnboarded)
                .Select(c => c.Id)
                .ToListAsync(ct);

            int granted = 0;
            foreach (Guid channelId in channelIds)
            {
                Result modResult = await moderators.AddModeratorAsync(channelId, bot.BotUserId, ct);
                if (modResult.IsSuccess)
                    granted++;
                else
                    logger.LogWarning(
                        "Bot mod-grant backfill: could not grant moderator status to the shared bot in {BroadcasterId}: {Error} ({Code})",
                        channelId,
                        modResult.ErrorMessage,
                        modResult.ErrorCode
                    );
            }

            logger.LogInformation(
                "Bot mod-grant backfill: shared bot {BotUsername} granted moderator status on {Granted}/{Total} onboarded channels",
                @event.BotUsername,
                granted,
                channelIds.Count
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Bot mod-grant backfill: failed for bot account {BotAccountId}",
                @event.BotAccountId
            );
        }
    }
}
