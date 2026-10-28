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
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Resolves a channel's dedicated Twitch bot in the same order the chat send path uses (<c>BotSelfEchoGuard</c>):
/// the channel's active custom bot authorization, else the shared platform bot once it is connected.
/// </summary>
public sealed class ChannelTwitchBotResolver(IApplicationDbContext db) : IChannelTwitchBotResolver
{
    public async Task<ChannelTwitchBot?> ResolveAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        ChannelTwitchBot? custom = await db
            .ChannelBotAuthorizations.IgnoreQueryFilters()
            .Where(a => a.BroadcasterId == broadcasterId && a.IsActive && a.DeletedAt == null)
            .Join(db.BotAccounts.IgnoreQueryFilters(), a => a.BotAccountId, b => b.Id, (_, b) => b)
            .Where(b =>
                b.IsActive && b.DeletedAt == null && b.Platform == AuthEnums.Platform.Twitch
            )
            .Select(b => new ChannelTwitchBot(b.BotUserId, b.BotUsername))
            .FirstOrDefaultAsync(cancellationToken);
        if (custom is not null)
            return custom;

        return await db
            .BotAccounts.IgnoreQueryFilters()
            .Where(b =>
                b.IdentityType == AuthEnums.BotIdentityType.Shared
                && b.IsActive
                && b.DeletedAt == null
                && b.ConnectionId != null
                && b.Platform == AuthEnums.Platform.Twitch
            )
            .Select(b => new ChannelTwitchBot(b.BotUserId, b.BotUsername))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
