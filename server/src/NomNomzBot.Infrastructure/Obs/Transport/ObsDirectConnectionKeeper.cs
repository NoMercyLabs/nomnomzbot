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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;

namespace NomNomzBot.Infrastructure.Obs.Transport;

/// <summary>
/// Keeps every enabled direct-mode OBS connection open (obs-control.md §3.2), so OBS events reach the
/// trigger surface and the dashboard without waiting for a first command. A channel whose dial fails
/// is retried with a doubling backoff (2 s up to 60 s); a drop is redialed on the next sweep.
/// </summary>
public sealed class ObsDirectConnectionKeeper : BackgroundService
{
    internal static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DialTimeout = TimeSpan.FromSeconds(10);

    private readonly DirectObsTransport _transport;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<ObsDirectConnectionKeeper> _logger;
    private readonly Dictionary<Guid, RetryState> _retry = [];

    public ObsDirectConnectionKeeper(
        DirectObsTransport transport,
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<ObsDirectConnectionKeeper> logger
    )
    {
        _transport = transport;
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "OBS direct connection sweep failed.");
            }

            await Task.Delay(SweepInterval, _clock, stoppingToken);
        }
    }

    /// <summary>One pass: dial each enabled direct channel that has no live session and whose backoff is due.</summary>
    internal async Task SweepAsync(CancellationToken ct)
    {
        List<Guid> channels;
        using (IServiceScope scope = _scopeFactory.CreateScope())
        {
            IApplicationDbContext db =
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            channels = await db
                .ObsConnections.Where(c => c.IsEnabled && c.Mode == "direct")
                .Select(c => c.BroadcasterId)
                .ToListAsync(ct);
        }

        foreach (Guid stale in _retry.Keys.Where(id => !channels.Contains(id)).ToList())
            _retry.Remove(stale);

        DateTimeOffset now = _clock.GetUtcNow();
        foreach (Guid channel in channels)
        {
            if (_transport.IsConnected(channel))
            {
                _retry.Remove(channel);
                continue;
            }

            if (_retry.TryGetValue(channel, out RetryState? state) && state.NextAttempt > now)
                continue;

            await DialAsync(channel, state, now, ct);
        }
    }

    private async Task DialAsync(
        Guid channel,
        RetryState? previous,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(DialTimeout);

        bool connected;
        try
        {
            connected = (await _transport.ConnectAsync(channel, timeout.Token)).IsSuccess;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "OBS direct dial threw for channel {ChannelId}.", channel);
            connected = false;
        }

        if (connected)
        {
            _retry.Remove(channel);
            return;
        }

        TimeSpan backoff = previous is null
            ? InitialBackoff
            : TimeSpan.FromTicks(Math.Min(previous.Backoff.Ticks * 2, MaxBackoff.Ticks));
        _retry[channel] = new(now + backoff, backoff);
    }

    private sealed record RetryState(DateTimeOffset NextAttempt, TimeSpan Backoff);
}
