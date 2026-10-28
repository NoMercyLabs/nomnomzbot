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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Contracts.Discord;

namespace NomNomzBot.Infrastructure.Discord;

/// <summary>
/// Runs once on startup and self-heals any Discord "currently live" role left applied after a missed
/// <c>stream.offline</c> event (a bot restart/crash between online and offline) — see
/// <see cref="IDiscordLiveRoleService.ReconcileStaleAsync"/>. Gated by <see cref="IRunOnceGuard"/> so a
/// zero-downtime deploy overlap (two API instances starting against one database) does not double the removal
/// calls — the same seam <see cref="Music.SongRequestQueueRestoreHostedService"/> uses.
/// </summary>
public sealed class DiscordLiveRoleReconciliationHostedService : IHostedService
{
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(5);

    internal const string LeaseResourceName = "discord-live-role-reconcile";

    private readonly IServiceScopeFactory _scopeFactory;

    public DiscordLiveRoleReconciliationHostedService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IRunOnceGuard guard = scope.ServiceProvider.GetRequiredService<IRunOnceGuard>();
        await using IAsyncDisposable? lease = await guard.TryAcquireAsync(
            LeaseResourceName,
            LeaseTtl,
            cancellationToken
        );
        if (lease is null)
            return; // another instance is reconciling this startup.

        IDiscordLiveRoleService liveRoleService =
            scope.ServiceProvider.GetRequiredService<IDiscordLiveRoleService>();
        await liveRoleService.ReconcileStaleAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
