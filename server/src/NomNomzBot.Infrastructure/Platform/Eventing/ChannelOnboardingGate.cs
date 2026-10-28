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
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// Reads <c>Channels.IsOnboarded</c> straight from the database on every ask (one primary-key lookup), so a
/// channel that onboards is open on the very next event with no stale cache. Singleton: it opens its own scope.
/// </summary>
public sealed class ChannelOnboardingGate(IServiceScopeFactory scopeFactory)
    : IChannelOnboardingGate
{
    public async Task<bool> IsClosedAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (broadcasterId == Guid.Empty)
            return false;

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        return await db.Channels.AnyAsync(
            c => c.Id == broadcasterId && !c.IsOnboarded,
            cancellationToken
        );
    }
}
