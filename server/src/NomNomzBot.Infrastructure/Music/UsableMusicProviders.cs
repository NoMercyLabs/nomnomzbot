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
using NomNomzBot.Domain.Music.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// The one rule for which registered music providers a channel can use: the ones it connected (an enabled,
/// token-bearing service row), plus the ones that work without a connection
/// (<see cref="IMusicProvider.WorksWithoutConnectionAsync"/>). Song-request routing and the public
/// song-request page both read it, so the page never offers a provider the bot cannot reach.
/// </summary>
internal static class UsableMusicProviders
{
    public static async Task<IReadOnlyList<IMusicProvider>> ForChannelAsync(
        IApplicationDbContext db,
        IEnumerable<IMusicProvider> providers,
        Guid channelId,
        CancellationToken cancellationToken
    )
    {
        List<string> connected = await db
            .Services.Where(s => s.BroadcasterId == channelId && s.Enabled && s.AccessToken != null)
            .Select(s => s.Name)
            .ToListAsync(cancellationToken);

        List<IMusicProvider> usable = [];
        foreach (IMusicProvider provider in providers)
        {
            if (
                connected.Contains(provider.Provider)
                || await provider.WorksWithoutConnectionAsync(cancellationToken)
            )
                usable.Add(provider);
        }
        return usable;
    }
}
