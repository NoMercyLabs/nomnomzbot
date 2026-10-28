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
        // Tokens live in the vault (S036c-b), so a live connected IntegrationConnections row is the truth.
        // The legacy Services row stays as well: Spotify is still mirrored there.
        List<string> connected = await db
            .IntegrationConnections.Where(c =>
                c.BroadcasterId == channelId
                && c.DeletedAt == null
                && c.Status == AuthEnums.IntegrationStatus.Connected
            )
            .Select(c => c.Provider)
            .ToListAsync(cancellationToken);
        connected.AddRange(
            await db
                .Services.Where(s =>
                    s.BroadcasterId == channelId && s.Enabled && s.AccessToken != null
                )
                .Select(s => s.Name)
                .ToListAsync(cancellationToken)
        );

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
