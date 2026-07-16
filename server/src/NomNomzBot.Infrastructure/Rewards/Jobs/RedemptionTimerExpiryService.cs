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
using NomNomzBot.Application.Rewards.Services;

namespace NomNomzBot.Infrastructure.Rewards.Jobs;

/// <summary>
/// Finishes expired redemption countdowns: every tick, timers whose clock-derived remaining time has
/// reached zero complete and their redemptions are fulfilled on Twitch (via
/// <see cref="IRedemptionTimerService.CompleteDueAsync"/>). The countdown itself never ticks in the
/// database — remaining time is clock math — so this only exists to fire the completion side effects
/// close to the moment they're due.
/// </summary>
public sealed class RedemptionTimerExpiryService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<RedemptionTimerExpiryService> _logger;

    public RedemptionTimerExpiryService(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<RedemptionTimerExpiryService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RedemptionTimerExpiryService started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
                await Task.Delay(TickInterval, _clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redemption timer expiry tick failed");
            }
        }
    }

    // Internal so tests can drive a single deterministic tick (InternalsVisibleTo is wired).
    internal async Task TickAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IRedemptionTimerService timers =
            scope.ServiceProvider.GetRequiredService<IRedemptionTimerService>();
        int completed = await timers.CompleteDueAsync(ct);
        if (completed > 0)
            _logger.LogInformation("Completed {Count} due redemption timer(s)", completed);
    }
}
