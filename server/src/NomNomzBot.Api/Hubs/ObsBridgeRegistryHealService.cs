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

using NomNomzBot.Application.Obs.Services;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// Every 15 seconds lists this node's live OBS bridges in the shared bridge book again. A command that times out
/// evicts its leader from the book, and during a deploy the old node can time out on a bridge that is connected to
/// the NEW node: the connection stays open but is never listed again, so every OBS action answers "No OBS bridge is
/// connected" until the bridge page reloads. A bridge already in the book is left alone, so this announces nothing
/// in the normal case. Failures are logged and the loop continues.
/// </summary>
public sealed class ObsBridgeRegistryHealService(
    IServiceScopeFactory scopes,
    TimeProvider timeProvider,
    ILogger<ObsBridgeRegistryHealService> logger,
    Func<
        IReadOnlyList<(string ConnectionId, Guid BroadcasterId, DateTime ConnectedAt)>
    >? liveBridges = null
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly Func<
        IReadOnlyList<(string ConnectionId, Guid BroadcasterId, DateTime ConnectedAt)>
    > _liveBridges = liveBridges ?? OBSRelayHub.LiveBridges;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await HealOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "OBS bridge book heal failed; continuing");
            }
        }
    }

    /// <summary>One pass: every live bridge of this node is listed (a no-op for one that already is).</summary>
    public async Task HealOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<(string ConnectionId, Guid BroadcasterId, DateTime ConnectedAt)> live =
            _liveBridges();
        if (live.Count == 0)
            return;

        using IServiceScope scope = scopes.CreateScope();
        IObsBridgeRegistry registry =
            scope.ServiceProvider.GetRequiredService<IObsBridgeRegistry>();
        foreach ((string connectionId, Guid broadcasterId, DateTime connectedAt) in live)
            await registry.RegisterAsync(broadcasterId, connectionId, connectedAt, ct);
    }
}
