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

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Identity.EventHandlers;

/// <summary>
/// Feeds Twitch moderator role changes (<c>channel.moderator.add</c> / <c>.remove</c>) into the bot's moderator
/// status, so the "bot is not a moderator" inbox item appears and clears the moment Twitch reports the change.
/// A change naming anyone other than the channel's bot is ignored by the status service.
/// </summary>
public sealed class BotModeratorRoleChangeHandler(
    IBotModeratorStatusService status,
    ILogger<BotModeratorRoleChangeHandler> logger
) : IEventHandler<ModeratorAddedEvent>, IEventHandler<ModeratorRemovedEvent>
{
    public Task HandleAsync(ModeratorAddedEvent @event, CancellationToken ct = default) =>
        ApplyAsync(@event.BroadcasterId, @event.UserId, isModerator: true, ct);

    public Task HandleAsync(ModeratorRemovedEvent @event, CancellationToken ct = default) =>
        ApplyAsync(@event.BroadcasterId, @event.UserId, isModerator: false, ct);

    private async Task ApplyAsync(
        Guid broadcasterId,
        string twitchUserId,
        bool isModerator,
        CancellationToken ct
    )
    {
        if (broadcasterId == Guid.Empty)
            return;

        Result applied = await status.ApplyRoleChangeAsync(
            broadcasterId,
            twitchUserId,
            isModerator,
            ct
        );
        if (applied.IsFailure)
            logger.LogWarning(
                "Bot moderator status: could not apply a moderator role change in {BroadcasterId}: {Error} ({Code})",
                broadcasterId,
                applied.ErrorMessage,
                applied.ErrorCode
            );
    }
}
