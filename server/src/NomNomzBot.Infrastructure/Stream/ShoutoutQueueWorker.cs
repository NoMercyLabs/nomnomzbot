// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Stream;

/// <summary>
/// Releases the shoutouts that waited out Twitch's global cooldown (old-bot parity: the legacy
/// ShoutoutQueueService). Each pass takes the head of every channel's queue once its global cooldown has
/// passed and sends it in full; a run that fails goes back once.
/// </summary>
public sealed class ShoutoutQueueWorker : BackgroundService
{
    private const int MaxAttempts = 20;
    private const string ShoutoutActionKey = "moderation:shoutout";
    private const string RaidSanction = "shoutout:raid-or-priority-step";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IShoutoutQueue _queue;
    private readonly IOutboundSanctionAccessor _sanctions;
    private readonly IChannelRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ShoutoutQueueWorker> _logger;

    public ShoutoutQueueWorker(
        IShoutoutQueue queue,
        IOutboundSanctionAccessor sanctions,
        IChannelRegistry registry,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<ShoutoutQueueWorker> logger
    )
    {
        _queue = queue;
        _sanctions = sanctions;
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
            // No HTTP request or chat pipeline stands behind a queued run, so it carries its own basis: the
            // moderator who asked, or the channel's own raid/priority step when no person asked.
            using IDisposable sanction = _sanctions.Begin(
                await SanctionForAsync(item, scope.ServiceProvider, cancellationToken)
            );
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

        // A rate limit or a dropped connection waits out its window and goes again; only a permanent error
        // (or a run that never succeeds in MaxAttempts passes) drops the item.
        if (result.Retryable && item.Attempts < MaxAttempts)
        {
            _logger.LogInformation(
                "Queued shoutout to {UserId} hit a temporary error and waits for the next window: {Error}",
                item.Target.Id,
                result.ErrorMessage
            );
            _queue.Enqueue(item with { Attempts = item.Attempts + 1 });
            return;
        }
        _logger.LogWarning(
            "Queued shoutout to {UserId} was not delivered and is dropped: {Error}",
            item.Target.Id,
            result.ErrorMessage
        );
    }

    private static async Task<OutboundSanction> SanctionForAsync(
        QueuedShoutout item,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        if (item.IsRaid)
            return OutboundSanction.ChannelConfiguration(RaidSanction);

        IApplicationDbContext db = services.GetRequiredService<IApplicationDbContext>();
        Guid? actor = await db
            .Users.AsNoTracking()
            .Where(u => u.TwitchUserId == item.TriggeredByUserId)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return OutboundSanction.UserAction(ShoutoutActionKey, actor);
    }
}
