// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
