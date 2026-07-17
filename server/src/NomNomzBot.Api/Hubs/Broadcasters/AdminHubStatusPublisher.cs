// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.SignalR;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// The operator hub's heartbeat: every 15 seconds pushes the REAL system snapshot (the same
/// <see cref="IAdminService"/> data the admin endpoints serve) as <c>ReceiveSystemStatus</c>, so the admin
/// dashboard's live panel moves without polling. Pushing to zero connections is a no-op at the SignalR layer,
/// so no connection tracking is needed. Failures are logged and the loop continues — the publisher never dies.
/// </summary>
public sealed class AdminHubStatusPublisher(
    IHubContext<AdminHub, IAdminClient> hub,
    IServiceScopeFactory scopeFactory,
    ILogger<AdminHubStatusPublisher> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                IAdminService admin = scope.ServiceProvider.GetRequiredService<IAdminService>();

                Result<AdminSystemDto> system = await admin.GetSystemHealthAsync(stoppingToken);
                Result<AdminStatsDto> stats = await admin.GetStatsAsync(stoppingToken);
                if (system.IsFailure || stats.IsFailure)
                    continue;

                await hub.Clients.All.ReceiveSystemStatus(
                    new { System = system.Value, Stats = stats.Value }
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "AdminHub status publish failed; continuing");
            }
        }
    }
}
