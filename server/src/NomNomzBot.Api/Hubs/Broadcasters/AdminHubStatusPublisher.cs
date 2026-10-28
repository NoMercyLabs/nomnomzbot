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
                await PublishOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "AdminHub status publish failed; continuing");
            }
        }
    }

    /// <summary>
    /// One heartbeat. The platform stats go only to connections holding <c>platform:analytics:read</c>
    /// (<see cref="AdminHub.AnalyticsGroup"/>); every other admin gets the health snapshot with
    /// <c>Stats</c> null (<see cref="AdminHub.NoAnalyticsGroup"/>).
    /// </summary>
    internal async Task PublishOnceAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IAdminService admin = scope.ServiceProvider.GetRequiredService<IAdminService>();

        Result<AdminSystemDto> system = await admin.GetSystemHealthAsync(ct);
        Result<AdminStatsDto> stats = await admin.GetStatsAsync(ct);
        if (system.IsFailure || stats.IsFailure)
            return;

        await hub
            .Clients.Group(AdminHub.AnalyticsGroup)
            .ReceiveSystemStatus(
                new { System = system.Value, Stats = (AdminStatsDto?)stats.Value }
            );
        await hub
            .Clients.Group(AdminHub.NoAnalyticsGroup)
            .ReceiveSystemStatus(new { System = system.Value, Stats = (AdminStatsDto?)null });
    }
}
