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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Platforms;

/// <summary>Reads the connections from the channel rows that share the render channel's owner.</summary>
internal sealed class DbPlatformDirectory : IPlatformDirectory
{
    public async Task<IReadOnlyList<PlatformLink>> ListAsync(
        IServiceProvider services,
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        IApplicationDbContext db = services.GetRequiredService<IApplicationDbContext>();
        Guid? ownerId = await db
            .Channels.AsNoTracking()
            .Where(c => c.Id == broadcasterId)
            .Select(c => (Guid?)c.OwnerUserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (ownerId is null)
            return [];

        List<PlatformLink> links = await db
            .Channels.AsNoTracking()
            .Where(c => c.OwnerUserId == ownerId && c.Enabled)
            .Select(c => new PlatformLink(
                c.Provider,
                c.Id,
                c.ExternalChannelId,
                c.NameNormalized,
                c.IsLive
            ))
            .ToListAsync(cancellationToken);

        return
        [
            .. links
                .Where(l => PlatformNames.Find(l.Provider) is not null)
                .GroupBy(l => l.Provider, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(l => l.ChannelId == broadcasterId).First())
                .OrderBy(l => PlatformNames.OrderOf(l.Provider)),
        ];
    }
}
