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
    ISharedChatSessionTracker sessions,
    IEventBus eventBus
) : IEventHandler<UserBannedEvent>
{
    public async Task HandleAsync(UserBannedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        SharedChatSessionInfo? session = sessions.GetActiveSession(@event.BroadcasterId);
        if (session is null)
            return; // not in a shared-chat session — a plain local ban

        bool sharing = await db.SharedBanSettings.AnyAsync(
            s => s.BroadcasterId == @event.BroadcasterId && s.ShareOutgoingBans,
            ct
        );
        if (!sharing)
            return; // the origin never opted in to offering its bans

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
