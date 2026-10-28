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
