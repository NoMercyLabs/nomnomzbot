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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Stream;

/// <summary>
/// Releases the shoutouts that waited out Twitch's global cooldown (old-bot parity: the legacy
/// ShoutoutQueueService). Each pass takes the head of every channel's queue once its global cooldown has
/// passed and sends it in full; a run that fails goes back once.
/// </summary>
public sealed class ShoutoutQueueWorker : BackgroundService
{
    private const int MaxRetries = 1;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IShoutoutQueue _queue;
    private readonly IChannelRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ShoutoutQueueWorker> _logger;

    public ShoutoutQueueWorker(
        IShoutoutQueue queue,
        IChannelRegistry registry,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<ShoutoutQueueWorker> logger
    )
    {
        _queue = queue;
        _registry = registry;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(PollInterval, _timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Shoutout queue pass failed");
            }
        }
    }

    public async Task ProcessDueAsync(CancellationToken cancellationToken)
    {
        foreach (Guid broadcasterId in _queue.ChannelsWithPending())
        {
            QueuedShoutout? head = _queue.Peek(broadcasterId);
            if (head is null)
                continue;

            ChannelContext? channel = _registry.Get(broadcasterId);
            DateTimeOffset now = _timeProvider.GetUtcNow();
            if (ShoutoutCooldowns.GlobalActive(channel, head.GlobalCooldown, now))
                continue;

            // Removed first: a live edge that clears the queue mid-run must not see the item again.
            if (!_queue.Remove(broadcasterId, head.Target.Id))
                continue;

            await RunAsync(
                head,
                ShoutoutCooldowns.PerUserActive(channel, head.Target.Id, head.PerUserCooldown, now),
                cancellationToken
            );
        }
    }

    private async Task RunAsync(
        QueuedShoutout item,
        bool skipNativeCall,
        CancellationToken cancellationToken
    )
    {
        ActionResult result;
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IShoutoutSender sender = scope.ServiceProvider.GetRequiredService<IShoutoutSender>();
            result = await sender.SendAsync(
                new(item.BroadcasterId, item.Target, item.Announcement, item.Speak, skipNativeCall),
                cancellationToken
            );
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            result = ActionResult.Failure(ex.Message);
        }

        if (result.Succeeded)
            return;

        if (item.Attempts >= MaxRetries)
        {
            _logger.LogWarning(
                "Queued shoutout to {UserId} failed again and is dropped: {Error}",
                item.Target.Id,
                result.ErrorMessage
            );
            return;
        }
        _queue.Enqueue(item with { Attempts = item.Attempts + 1 });
    }
}
