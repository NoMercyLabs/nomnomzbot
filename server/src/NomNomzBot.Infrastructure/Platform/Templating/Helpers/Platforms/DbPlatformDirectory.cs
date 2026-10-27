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
