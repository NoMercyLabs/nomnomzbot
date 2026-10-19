// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// Runs <see cref="IMusicPlayerMigrationService"/> once per start. Gated by <see cref="IRunOnceGuard"/> so a
/// zero-downtime deploy overlap (two API instances against one database) does not run it twice.
/// </summary>
public sealed class MusicPlayerMigrationHostedService(IServiceScopeFactory scopeFactory)
    : IHostedService
{
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(10);

    internal const string LeaseResourceName = "music-player-migration";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IRunOnceGuard guard = scope.ServiceProvider.GetRequiredService<IRunOnceGuard>();
        await using IAsyncDisposable? lease = await guard.TryAcquireAsync(
            LeaseResourceName,
            LeaseTtl,
            cancellationToken
        );
        if (lease is null)
            return; // another instance is migrating this startup.

        IMusicPlayerMigrationService migration =
            scope.ServiceProvider.GetRequiredService<IMusicPlayerMigrationService>();
        await migration.MigrateAllChannelsAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
