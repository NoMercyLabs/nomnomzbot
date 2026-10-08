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

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// Keeps the AutoMod review queue honest: every few minutes, a pending AutoMod row that is older than
/// <see cref="StaleHoldBackstop"/> flips to <c>expired</c>. Twitch drops a held message by itself and normally
/// says so in <c>automod.message.update</c>; this sweep covers the update we never received (a missed event, a
/// deploy gap). Idempotent under <see cref="IRunOnceGuard"/> so multi-instance deployments sweep once.
/// </summary>
public sealed class AutoModQueueExpiryWorker : BackgroundService
{
    // A backstop for a missed automod.message.update, not Twitch's hold window (Twitch does not document one).
    internal static readonly TimeSpan StaleHoldBackstop = TimeSpan.FromHours(1);

    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<AutoModQueueExpiryWorker> _logger;

    public AutoModQueueExpiryWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<AutoModQueueExpiryWorker> logger
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
                    await SweepAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "AutoMod queue expiry sweep failed");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
    }

    // Internal so tests can drive one deterministic sweep (InternalsVisibleTo is already wired).
    internal async Task SweepAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IRunOnceGuard guard = scope.ServiceProvider.GetRequiredService<IRunOnceGuard>();
        await using IAsyncDisposable? lease = await guard.TryAcquireAsync(
            "automod-queue-expiry",
            LeaseTtl,
            ct
        );
        if (lease is null)
            return; // another instance is sweeping.

        IModerationQueueService queue =
            scope.ServiceProvider.GetRequiredService<IModerationQueueService>();
        int expired = await queue.ExpireStaleAutoModAsync(StaleHoldBackstop, ct);
        if (expired > 0)
            _logger.LogInformation(
                "AutoMod queue expiry closed {Count} stale pending row(s)",
                expired
            );
    }
}
