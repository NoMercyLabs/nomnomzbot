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

namespace NomNomzBot.Infrastructure.Moderation.MassBan;

/// <summary>
/// Runs <see cref="MassBanExecutor"/> every few seconds on the active instance only, under a run-once lease so two
/// instances never ban the same list twice.
/// </summary>
public sealed class MassBanExecutionWorker : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(1);

    // 25 bans per 5 seconds is 300 a minute: well inside Twitch's 800-point-a-minute bucket of the moderator's
    // token, so the moderator's own dashboard keeps working while a list runs.
    internal const int BansPerTick = 25;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<MassBanExecutionWorker> _logger;

    public MassBanExecutionWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<MassBanExecutionWorker> logger
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
                    _logger.LogError(ex, "Mass-ban execution tick failed");
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
            "massban-execution",
            LeaseTtl,
            ct
        );
        if (lease is null)
            return;

        MassBanExecutor executor = scope.ServiceProvider.GetRequiredService<MassBanExecutor>();
        await executor.RunAsync(BansPerTick, ct);
    }
}
