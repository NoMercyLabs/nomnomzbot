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

namespace NomNomzBot.Infrastructure.Platform.Scheduling;

public class TokenRefreshService : BackgroundService
{
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
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(30));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using IServiceScope scope = _serviceProvider.CreateScope();
                ITwitchAuthService authService =
                    scope.ServiceProvider.GetRequiredService<ITwitchAuthService>();
                await authService.RefreshExpiringTokensAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error refreshing OAuth tokens.");
            }
        }
    }
}
