// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Eventing;

namespace NomNomzBot.Infrastructure.Music.Realtime;

/// <summary>
/// Owns one <see cref="SpotifyDealerConnection"/> per channel that should have one — every channel with a
/// non-revoked Spotify connection whose <c>PreferredProvider</c> is not YouTube. Re-checks eligibility every
/// <see cref="RediscoveryInterval"/> so a channel connecting/disconnecting Spotify, or flipping its preferred
/// provider, starts/stops its socket without a process restart. <c>MusicStatePollingService</c> is started
/// independently (see its own DI registration) and never depends on this service — a channel this service
/// skips (no token, socket unauthorised, provider is YouTube) is simply never given a connection here, and
/// keeps being served by the poller exactly as if this service did not exist.
/// </summary>
public sealed class SpotifyDealerHostedService : BackgroundService
{
    private static readonly TimeSpan RediscoveryInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWebSocketChannelFactory _channelFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEventBus _eventBus;
    private readonly IMusicRealtimeSignal _realtime;
    private readonly ISongRequestQueueStore _queueStore;
    private readonly TimeProvider _clock;
    private readonly ILogger<SpotifyDealerHostedService> _logger;

    private readonly ConcurrentDictionary<Guid, RunningConnection> _running = new();

    public SpotifyDealerHostedService(
        IServiceScopeFactory scopeFactory,
        IWebSocketChannelFactory channelFactory,
        IHttpClientFactory httpClientFactory,
        IEventBus eventBus,
        IMusicRealtimeSignal realtime,
        ISongRequestQueueStore queueStore,
        TimeProvider clock,
        ILogger<SpotifyDealerHostedService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _channelFactory = channelFactory;
        _httpClientFactory = httpClientFactory;
        _eventBus = eventBus;
        _realtime = realtime;
        _queueStore = queueStore;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(RediscoveryInterval, _clock);
        bool keepGoing = true;
        while (keepGoing)
        {
            try
            {
                await ReconcileConnectionsAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "SpotifyDealerHostedService: reconcile failed");
            }

            try
            {
                keepGoing = await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                keepGoing = false;
            }
        }

        foreach (RunningConnection running in _running.Values)
            await StopConnectionAsync(running);
        _running.Clear();
    }

    /// <summary>Every channel a dealer connection should be running for right now: a non-revoked Spotify
    /// connection whose channel does not prefer YouTube. Internal (not private) so tests can drive discrete
    /// reconciliation passes directly instead of waiting on the real <see cref="PeriodicTimer"/>.</summary>
    internal async Task<List<Guid>> LoadEligibleChannelsAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        IMusicConfigService configs =
            scope.ServiceProvider.GetRequiredService<IMusicConfigService>();

        List<Guid> spotifyConnected = await db
            .IntegrationConnections.Where(c =>
                c.Provider == AuthEnums.IntegrationProvider.Spotify
                && c.BroadcasterId != null
                && c.Status != AuthEnums.IntegrationStatus.Revoked
            )
            .Select(c => c.BroadcasterId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        List<Guid> eligible = [];
        foreach (Guid channelId in spotifyConnected)
        {
            Result<MusicConfigDto> config = await configs.GetConfigAsync(
                channelId.ToString(),
                cancellationToken
            );
            // A channel that prefers YouTube gets no Spotify dealer socket — the dealer transport is
            // Spotify-only, and the poller already covers YouTube's documented API on its own.
            if (
                config is
                { IsSuccess: true, Value.PreferredProvider: AuthEnums.IntegrationProvider.YouTube }
            )
                continue;

            eligible.Add(channelId);
        }

        return eligible;
    }

    internal async Task ReconcileConnectionsAsync(CancellationToken stoppingToken)
    {
        List<Guid> eligible = await LoadEligibleChannelsAsync(stoppingToken);
        HashSet<Guid> eligibleSet = [.. eligible];

        foreach (Guid channelId in eligible)
            _running.GetOrAdd(channelId, id => StartConnection(id, stoppingToken));

        foreach (KeyValuePair<Guid, RunningConnection> entry in _running)
        {
            if (eligibleSet.Contains(entry.Key))
                continue;

            if (_running.TryRemove(entry.Key, out RunningConnection? removed))
                await StopConnectionAsync(removed);
        }
    }

    private RunningConnection StartConnection(Guid channelId, CancellationToken hostToken)
    {
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
        SpotifyDealerConnection connection = new(
            channelId,
            _channelFactory,
            _httpClientFactory.CreateClient("spotify"),
            ct => ResolveAccessTokenAsync(channelId, ct),
            _eventBus,
            _realtime,
            _queueStore,
            _clock,
            _logger
        );
        Task run = connection.RunAsync(cts.Token);
        return new RunningConnection(connection, cts, run);
    }

    private async Task<string?> ResolveAccessTokenAsync(Guid broadcasterId, CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        IIntegrationTokenVault vault =
            scope.ServiceProvider.GetRequiredService<IIntegrationTokenVault>();

        Guid? connectionId = await db
            .IntegrationConnections.Where(c =>
                c.Provider == AuthEnums.IntegrationProvider.Spotify
                && c.BroadcasterId == broadcasterId
                && c.Status != AuthEnums.IntegrationStatus.Revoked
            )
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
        if (connectionId is null)
            return null;

        Result<DecryptedTokenDto> token = await vault.GetAccessTokenAsync(connectionId.Value, ct);
        return token.IsSuccess ? token.Value.Value : null;
    }

    private static async Task StopConnectionAsync(RunningConnection running)
    {
        await running.Cts.CancelAsync();
        try
        {
            await running.RunTask;
        }
        catch (OperationCanceledException) { }
        running.Cts.Dispose();
    }

    private sealed record RunningConnection(
        SpotifyDealerConnection Connection,
        CancellationTokenSource Cts,
        Task RunTask
    );
}
