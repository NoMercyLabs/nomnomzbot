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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Games;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Games.EventHandlers;

/// <summary>
/// The engine's SINGLE chat subscription (live-games.md D6) — one dispatcher over the canonical chat fact,
/// never a listener per game. The hot path is a lock-free registry lookup plus a first-token keyword match;
/// only a matching message in an active round resolves the viewer and enters the engine.
/// </summary>
public sealed class LiveGameInputListener(
    LiveGameSessionRegistry registry,
    IServiceScopeFactory scopeFactory,
    IUserService userService
) : IEventHandler<ChatMessageReceivedEvent>
{
    public async Task HandleAsync(
        ChatMessageReceivedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty || string.IsNullOrWhiteSpace(@event.Message))
            return;
        if (
            !registry.TryGet(@event.BroadcasterId, out LiveGameSessionRuntime? runtime)
            || runtime.Terminal
            || runtime.Phase is not (LiveGamePhase.Lobby or LiveGamePhase.Running)
        )
            return;

        string first = @event.Message.TrimStart().Split(' ', 2)[0];
        bool matches = runtime.Game.Manifest.InputKeywords.Any(k =>
            string.Equals(k, first, StringComparison.OrdinalIgnoreCase)
        );
        if (!matches)
            return;

        Result<UserDto> user = await userService.GetOrCreateAsync(
            @event.UserId,
            @event.UserLogin,
            @event.UserDisplayName,
            @event.Provider,
            cancellationToken
        );
        if (user.IsFailure || !Guid.TryParse(user.Value.Id, out Guid viewerUserId))
            return;

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        LiveGameEngine engine = scope.ServiceProvider.GetRequiredService<LiveGameEngine>();
        await engine.HandleChatInputAsync(
            @event.BroadcasterId,
            viewerUserId,
            @event.UserDisplayName,
            @event.Message,
            cancellationToken
        );
    }
}
