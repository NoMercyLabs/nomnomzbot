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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat;

namespace NomNomzBot.Infrastructure.Moderation.EventHandlers;

/// <summary>
/// The OUTGOING half of the shared-ban trust web (moderation.md §3.5): when a ban lands in a channel that
/// is (1) currently in a shared-chat session and (2) opted in to sharing (<c>ShareOutgoingBans</c>), offer
/// it to the web as a <see cref="SharedChatBanIssuedEvent"/>. Whether any partner APPLIES it is the
/// partners' own predicate (accept + trust + same session) — this publisher only offers.
/// </summary>
public sealed class SharedChatBanSharePublisher(
    IApplicationDbContext db,
    ISharedChatSessionRestorer restorer,
    IEventBus eventBus
) : IEventHandler<UserBannedEvent>
{
    public async Task HandleAsync(UserBannedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        // Cheap checks first: the Helix restore below is only worth a call for a channel that shares its bans.
        bool sharing = await db.SharedBanSettings.AnyAsync(
            s => s.BroadcasterId == @event.BroadcasterId && s.ShareOutgoingBans,
            ct
        );
        if (!sharing)
            return; // the origin never opted in to offering its bans

        SharedChatSessionInfo? session = await restorer.EnsureActiveSessionAsync(
            @event.BroadcasterId,
            ct
        );
        if (session is null)
            return; // not in a shared-chat session — a plain local ban

        await eventBus.PublishAsync(
            new SharedChatBanIssuedEvent
            {
                BroadcasterId = @event.BroadcasterId,
                SharedChatSessionId = session.SessionId,
                OriginChannelId = @event.BroadcasterId,
                TargetTwitchUserId = @event.TargetUserId,
                TargetDisplayName = @event.TargetDisplayName,
                Reason = @event.Reason,
            },
            ct
        );
    }
}
