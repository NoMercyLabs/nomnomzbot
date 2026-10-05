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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Scheduling;

public class TokenRefreshService : BackgroundService
{
    /// <summary>
    /// How often the proactive sweep runs. <c>TwitchAuthService.RefreshExpiringTokensAsync</c> derives its
    /// refresh window from this value (two intervals), so the window can never shrink below one missed tick.
    /// </summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(30);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TokenRefreshService> _logger;

    public TokenRefreshService(
        IServiceProvider serviceProvider,
        ILogger<TokenRefreshService> logger
    )
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Token refresh service started.");
        using PeriodicTimer timer = new(SweepInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RefreshOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error refreshing OAuth tokens.");
            }
        }
    }

    /// <summary>
    /// One sweep. Blue/green overlap: only the ACTIVE instance refreshes — the refresh gate is per process,
    /// so two colours would otherwise redeem the same refresh tokens at the same time.
    /// Internal so tests can drive a deterministic tick.
    /// </summary>
    internal async Task RefreshOnceAsync(CancellationToken ct)
    {
        using IServiceScope scope = _serviceProvider.CreateScope();
        IActiveInstanceGate? instanceGate = scope.ServiceProvider.GetService<IActiveInstanceGate>();
        if (instanceGate is { IsActiveInstance: false })
            return;

        ITwitchAuthService authService =
            scope.ServiceProvider.GetRequiredService<ITwitchAuthService>();
        await authService.RefreshExpiringTokensAsync(ct);
    }
}
