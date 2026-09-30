// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Moderation.EventHandlers;

/// <summary>
/// Chat ingest check for network blocks: a network-blocked actor who chats in a tenant the apply fan-out
/// never reached is banned there on the spot. Twitch only — a network block names a Twitch account and its
/// legs are Twitch bans.
/// </summary>
public sealed class NetworkBlockChatEnforcementHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<NetworkBlockChatEnforcementHandler> logger
) : IEventHandler<ChatMessageReceivedEvent>
{
    public async Task HandleAsync(ChatMessageReceivedEvent @event, CancellationToken ct = default)
    {
        if (
            @event.BroadcasterId == Guid.Empty
            || @event.Provider != AuthEnums.Platform.Twitch
            || @event.IsBroadcaster
        )
            return;

        try
        {
            // Own scope: chat handlers run side by side, and a DbContext must never be shared across them.
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            Result<int> enforced = await scope
                .ServiceProvider.GetRequiredService<INetworkBlockEnforcementService>()
                .EnforceForChatterAsync(@event.BroadcasterId, @event.UserId, ct);

            if (enforced is { IsSuccess: true, Value: > 0 })
                logger.LogWarning(
                    "Network block: banned {User} in {Channel} at chat ingest",
                    @event.UserLogin,
                    @event.BroadcasterId
                );
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Never take down chat ingestion: this handler shares the hot path with the chat feed.
            logger.LogError(ex, "Network block check failed for {Channel}", @event.BroadcasterId);
        }
    }
}
