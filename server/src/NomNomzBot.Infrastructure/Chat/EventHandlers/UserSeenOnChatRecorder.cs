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
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Chat.EventHandlers;

/// <summary>
/// Keeps <see cref="User.Color"/> and <see cref="User.LastSeenAt"/> current from the viewer's own chat messages.
/// Both columns were never written, so most profiles showed no color and no "last seen". The color follows the
/// message (an empty color keeps the stored one); <see cref="User.LastSeenAt"/> moves at most once per
/// <see cref="Throttle"/> so a busy chatter costs one write a minute, not one per line. The row is created by the
/// other chat handlers; a sender with no row yet is picked up on their next message.
/// </summary>
public sealed class UserSeenOnChatRecorder(IApplicationDbContext db)
    : IEventHandler<ChatMessageReceivedEvent>
{
    private static readonly TimeSpan Throttle = TimeSpan.FromMinutes(1);

    public async Task HandleAsync(
        ChatMessageReceivedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.Provider != AuthEnums.Platform.Twitch || string.IsNullOrEmpty(@event.UserId))
            return;

        User? user = await db.Users.FirstOrDefaultAsync(
            u => u.TwitchUserId == @event.UserId,
            cancellationToken
        );
        if (user is null)
            return;

        DateTime seenAt = @event.OccurredAt.UtcDateTime;
        bool colorChanged = !string.IsNullOrEmpty(@event.ColorHex) && user.Color != @event.ColorHex;
        bool seenStale = user.LastSeenAt is null || seenAt - user.LastSeenAt >= Throttle;
        if (!colorChanged && !seenStale)
            return;

        if (colorChanged)
            user.Color = @event.ColorHex;
        if (seenStale)
            user.LastSeenAt = seenAt;

        await db.SaveChangesAsync(cancellationToken);
    }
}
