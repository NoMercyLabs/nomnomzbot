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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Moderation.Services;

namespace NomNomzBot.Infrastructure.Moderation.Lockdown;

/// <summary>
/// Puts the room back when a lockdown window runs out (spam-defense.md §L5.1, SD12). Every few seconds it asks
/// <see cref="ILockdownService.RestoreDueAsync"/> to restore the windows that have expired, were ended, or whose
/// last restore left a control behind. Runs on the active instance only, under a run-once lease, so two
/// instances never restore the same window at once. A failed sweep is logged and the next tick tries again.
/// </summary>
public sealed class LockdownExpiryWorker : BackgroundService
{
    internal static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<LockdownExpiryWorker> _logger;

    public LockdownExpiryWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<LockdownExpiryWorker> logger
    )
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TickInterval, _clock);
        try
        {
            do
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Lockdown expiry sweep failed");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
    }

    internal async Task TickAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IActiveInstanceGate? instanceGate = scope.ServiceProvider.GetService<IActiveInstanceGate>();
        if (instanceGate is { IsActiveInstance: false })
            return;

        IRunOnceGuard guard = scope.ServiceProvider.GetRequiredService<IRunOnceGuard>();
        await using IAsyncDisposable? lease = await guard.TryAcquireAsync(
            "lockdown-expiry",
            LeaseTtl,
            ct
        );
        if (lease is null)
            return;

        ILockdownService lockdown = scope.ServiceProvider.GetRequiredService<ILockdownService>();
        int restored = await lockdown.RestoreDueAsync(ct);
        if (restored > 0)
            _logger.LogInformation("Lockdown expiry sweep restored {Count} window(s)", restored);
    }
}
