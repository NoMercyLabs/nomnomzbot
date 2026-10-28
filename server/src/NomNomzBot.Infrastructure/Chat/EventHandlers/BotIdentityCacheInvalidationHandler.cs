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

using NomNomzBot.Application.Contracts.Chat;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Chat.EventHandlers;

/// <summary>
/// Evicts <see cref="BotSelfEchoGuard"/>'s per-tenant bot-identity cache when a bot account connects or
/// disconnects (S009). <c>BroadcasterId == Guid.Empty</c> is the shared-platform-bot sentinel (schema
/// platform-conventions §2.0) — that bot applies to every tenant without a custom bot, so it evicts the
/// whole cache; a non-empty <c>BroadcasterId</c> is a per-channel custom bot and evicts only that tenant.
/// </summary>
public sealed class BotIdentityCacheInvalidationHandler(IBotIdentityCacheInvalidation cache)
    : IEventHandler<BotAccountAuthorizedEvent>,
        IEventHandler<BotAccountDisconnectedEvent>
{
    public Task HandleAsync(
        BotAccountAuthorizedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        Invalidate(@event.BroadcasterId);
        return Task.CompletedTask;
    }

    public Task HandleAsync(
        BotAccountDisconnectedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        Invalidate(@event.BroadcasterId);
        return Task.CompletedTask;
    }

    private void Invalidate(Guid broadcasterId)
    {
        if (broadcasterId == Guid.Empty)
            cache.InvalidateAll();
        else
            cache.InvalidateTenant(broadcasterId);
    }
}
