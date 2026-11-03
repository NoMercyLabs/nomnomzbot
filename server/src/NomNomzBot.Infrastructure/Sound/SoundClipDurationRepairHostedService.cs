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

namespace NomNomzBot.Infrastructure.Sound;

/// <summary>
/// Runs <see cref="SoundClipDurationRepair"/> once per start. Gated by <see cref="IRunOnceGuard"/> so a
/// zero-downtime deploy overlap (two API instances against one database) does not run it twice. A later start
/// finds every length right and changes nothing.
/// </summary>
public sealed class SoundClipDurationRepairHostedService(IServiceScopeFactory scopeFactory)
    : IHostedService
{
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(10);

    internal const string LeaseResourceName = "sound-clip-duration-repair";

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
            return; // another instance is repairing this startup.

        SoundClipDurationRepair repair =
            scope.ServiceProvider.GetRequiredService<SoundClipDurationRepair>();
        await repair.RunAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
