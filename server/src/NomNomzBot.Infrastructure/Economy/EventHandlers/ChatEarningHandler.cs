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
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Economy.EventHandlers;

/// <summary>
/// Awards currency for each chat message when the channel has a <c>ChatMessage</c> earning rule enabled
/// (economy.md §3.3, EarningSource.ChatMessage). The earn is idempotent by <c>MessageId</c> converted to a
/// deterministic <see cref="Guid"/> — a duplicate EventSub delivery for the same message will be a no-op in the
/// ledger. Uses <see cref="IServiceScopeFactory"/> to create its own scope so the earning service's
/// <see cref="NomNomzBot.Application.Abstractions.Persistence.IApplicationDbContext"/> never contends with the
/// parallel <c>ChatMessagePersistenceHandler</c> that runs in the EventBus dispatch scope.
/// </summary>
public sealed class ChatEarningHandler(IServiceScopeFactory scopeFactory, IUserService userService)
    : IEventHandler<ChatMessageReceivedEvent>
{
    public async Task HandleAsync(
        ChatMessageReceivedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty || string.IsNullOrEmpty(@event.UserId))
            return;

        // Resolve or create the viewer's internal User row — uses its own DB scope internally. The
        // event's Provider keys the identity namespace, so a YouTube chatter mints a youtube identity.
        Result<UserDto> userResult = await userService.GetOrCreateAsync(
            @event.UserId,
            @event.UserLogin,
            @event.UserDisplayName,
            @event.Provider,
            cancellationToken
        );
        if (userResult.IsFailure || !Guid.TryParse(userResult.Value.Id, out Guid viewerUserId))
            return;

        // Derive a stable Guid from the MessageId so the earn is idempotent.
        Guid? eventId = Guid.TryParse(@event.MessageId, out Guid parsed) ? parsed : null;

        // Resolve role level from badge flags (mirrors CommunityStanding ordinal for gate checks).
        int roleLevel = ResolveRoleLevel(@event);

        // Dedicated scope so CurrencyEarningService.IApplicationDbContext never races with
        // ChatMessagePersistenceHandler which holds the EventBus dispatch scope's DbContext.
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        ICurrencyEarningService earning =
            scope.ServiceProvider.GetRequiredService<ICurrencyEarningService>();

        await earning.ApplyEarningAsync(
            @event.BroadcasterId,
            new EarnRequest(viewerUserId, "ChatMessage", 1, eventId, roleLevel, Context: null),
            cancellationToken
        );
    }

    private static int ResolveRoleLevel(ChatMessageReceivedEvent @event)
    {
        if (@event.IsBroadcaster)
            return 5; // Broadcaster
        if (@event.IsModerator)
            return 3; // Moderator
        if (@event.IsVip)
            return 2; // VIP
        if (@event.IsSubscriber)
            return 1; // Subscriber
        return 0; // Everyone
    }
}
