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
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Runs once on startup, before any chat command or dashboard request can reach the (empty, freshly
/// constructed) <see cref="SongRequestQueueStore"/>, and replays every channel's still-fresh persisted
/// song-request queue back into memory (S001b). A channel whose persisted queue is stale (untouched
/// since before <see cref="FreshnessWindow"/>) is deliberately NOT restored — a queue frozen mid-stream
/// days ago is worse than starting empty — and is told instead via a logged warning and a published
/// <see cref="SongRequestQueueRestoreDiscardedEvent"/>.
///
/// Gated by <see cref="IRunOnceGuard"/> so that when two API instances start against one database
/// (zero-downtime deploy overlap) only one of them replays the persisted queue and publishes the
/// discarded-queue events — otherwise a second instance racing the same startup window would double
/// every restored channel's queue and double the discarded-queue notification. A non-holder is a
/// clean no-op: no throw, no error log — overlap is normal.
/// </summary>
public sealed class SongRequestQueueRestoreHostedService : IHostedService
{
    /// <summary>
    /// A channel's queue is trusted for restore only if it was touched within this window of "now" —
    /// generous enough to cover a normal stream's length plus restart/update downtime, short enough
    /// that a queue from a stream days ago never resurfaces as if it were still pending.
    /// </summary>
    public static readonly TimeSpan FreshnessWindow = TimeSpan.FromHours(4);

    private static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(5);

    // Internal (not private) so tests can pre-hold the same lease to simulate an overlapping instance —
    // InternalsVisibleTo(NomNomzBot.Infrastructure.Tests) is already wired for exactly this seam.
    internal const string LeaseResourceName = "song-request-queue-restore";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SongRequestQueueRestoreHostedService> _logger;

    public SongRequestQueueRestoreHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<SongRequestQueueRestoreHostedService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IRunOnceGuard guard = scope.ServiceProvider.GetRequiredService<IRunOnceGuard>();
        await using IAsyncDisposable? lease = await guard.TryAcquireAsync(
            LeaseResourceName,
            LeaseTtl,
            cancellationToken
        );
        if (lease is null)
            return; // another instance is restoring the queue this startup.

        ISongRequestQueuePersistence persistence =
            scope.ServiceProvider.GetRequiredService<ISongRequestQueuePersistence>();
        ISongRequestQueueStore store =
            scope.ServiceProvider.GetRequiredService<ISongRequestQueueStore>();
        IEventBus eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        SongRequestQueueRestoreResult result = await persistence.LoadForRestoreAsync(
            FreshnessWindow,
            cancellationToken
        );

        foreach (RestoredSongRequestQueue channel in result.Channels)
        {
            store.Restore(channel.BroadcasterId, channel.OrderedEntries, channel.InFlightIndex);
            _logger.LogInformation(
                "Restored {Count} pending song request(s) for channel {BroadcasterId} after restart",
                channel.OrderedEntries.Count,
                channel.BroadcasterId
            );
        }

        foreach (string broadcasterId in result.DiscardedStaleBroadcasterIds)
        {
            string reason =
                $"the persisted song-request queue was last touched over {FreshnessWindow.TotalHours:0}h ago and was discarded rather than restored stale";
            _logger.LogWarning(
                "Song-request queue for channel {BroadcasterId} could not be restored: {Reason}",
                broadcasterId,
                reason
            );

            if (Guid.TryParse(broadcasterId, out Guid tenantId))
            {
                await eventBus.PublishAsync(
                    new SongRequestQueueRestoreDiscardedEvent
                    {
                        BroadcasterId = tenantId,
                        Reason = reason,
                    },
                    cancellationToken
                );
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
