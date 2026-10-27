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
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Persistence;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Emotes;

/// <summary>The chat table and the channel table behind <see cref="IEmoteChatSource"/>.</summary>
internal sealed class DbEmoteChatSource : IEmoteChatSource
{
    public async Task<IReadOnlyList<EmoteChatLine>> RecentLinesAsync(
        IServiceProvider services,
        Guid broadcasterId,
        int take,
        CancellationToken cancellationToken
    )
    {
        IApplicationDbContext db = services.GetRequiredService<IApplicationDbContext>();
        return await db
            .ChatMessages.AsNoTracking()
            .Where(m => m.BroadcasterId == broadcasterId && !m.IsCommand)
            .OrderByDescending(m => m.CreatedAt)
            .Take(take)
            .Select(m => new EmoteChatLine(m.UserId, m.Message, m.StreamId))
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid?> OwnerUserIdAsync(
        IServiceProvider services,
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        IApplicationDbContext db = services.GetRequiredService<IApplicationDbContext>();
        return await db
            .Channels.AsNoTracking()
            .Where(c => c.Id == broadcasterId)
            .Select(c => (Guid?)c.OwnerUserId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
