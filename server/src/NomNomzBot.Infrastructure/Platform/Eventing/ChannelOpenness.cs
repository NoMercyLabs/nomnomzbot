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
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// The one rule for "may the bot act in this channel?", shared by the event bus gate and the chat router.
/// A channel is open when it is onboarded, or when an onboarded user (one who owns an onboarded channel)
/// holds a moderator-or-higher management membership in it. Every <c>ManagementRole</c> is that level.
/// </summary>
public static class ChannelOpenness
{
    public static Task<bool> IsClosedAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        int moderator = PermissionLevel.Moderator.ToLevelValue();

        return db.Channels.AnyAsync(
            c =>
                c.Id == broadcasterId
                && !c.IsOnboarded
                && !db.ChannelMemberships.Any(m =>
                    m.BroadcasterId == broadcasterId
                    && m.LevelValue >= moderator
                    && db.Channels.Any(home => home.OwnerUserId == m.UserId && home.IsOnboarded)
                ),
            cancellationToken
        );
    }
}
