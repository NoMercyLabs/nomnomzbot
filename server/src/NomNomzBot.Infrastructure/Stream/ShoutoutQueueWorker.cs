// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
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
/// Releases the shoutouts that waited out Twitch's cooldowns. Each pass takes the head of every channel's
/// queue once both windows have passed and sends it in full; a transient native failure stays queued.
/// </summary>
public sealed class ShoutoutQueueWorker : BackgroundService
{
    private const string ShoutoutActionKey = "moderation:shoutout";
    private const string RaidSanction = "shoutout:raid-or-priority-step";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IShoutoutQueue _queue;
    private readonly IOutboundSanctionAccessor _sanctions;
    private readonly IChannelRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ShoutoutQueueWorker> _logger;
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly Dictionary<
        (Guid BroadcasterId, string TargetId),
        DateTimeOffset
    > _nextRetryAt = [];

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
        await _runGate.WaitAsync(cancellationToken);
        try
        {
            await _queue.RecoverAsync(cancellationToken);
            foreach (Guid broadcasterId in _queue.ChannelsWithPending())
            {
                QueuedShoutout? head = _queue.Peek(broadcasterId);
                if (head is null)
                    continue;

                if (await WasRecordedAsync(head, cancellationToken))
                {
                    await _queue.RemoveAsync(broadcasterId, head.Target.Id, cancellationToken);
                    continue;
                }

                ChannelContext? channel = _registry.Get(broadcasterId);
                DateTimeOffset now = _timeProvider.GetUtcNow();
                if (head.NotBefore is { } notBefore && now < notBefore)
                    continue;
                if (
                    _nextRetryAt.TryGetValue(
                        (broadcasterId, head.Target.Id),
                        out DateTimeOffset retryAt
                    )
                    && now < retryAt
                )
                    continue;
                bool nativeAttempted = await WasAttemptedAsync(head, cancellationToken);
                if (
                    !nativeAttempted
                    && (
                        ShoutoutCooldowns.GlobalActive(channel, head.GlobalCooldown, now)
                        || ShoutoutCooldowns.PerUserActive(
                            channel,
                            head.Target.Id,
                            head.PerUserCooldown,
                            now
                        )
                    )
                )
                    continue;

                await RunAsync(head, cancellationToken);
            }
        }
        finally
        {
            _runGate.Release();
        }
    }

    private async Task<bool> WasRecordedAsync(
        QueuedShoutout item,
        CancellationToken cancellationToken
    )
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        List<string> data = await db
            .ChannelEvents.AsNoTracking()
            .Where(e =>
                e.ChannelId == item.BroadcasterId
                && e.Type == "bot.shoutout.sent"
                && e.CreatedAt >= item.EnqueuedAt.UtcDateTime
                && e.Data != null
            )
            .Select(e => e.Data!)
            .ToListAsync(cancellationToken);
        foreach (string payload in data)
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (
                document.RootElement.TryGetProperty("toUserId", out JsonElement target)
                && target.ValueKind == JsonValueKind.String
                && target.GetString() == item.Target.Id
                && document.RootElement.TryGetProperty("source", out JsonElement source)
                && source.ValueKind == JsonValueKind.String
                && source.GetString() is "pipeline" or "replay-uncertain"
            )
                return true;
        }
        return false;
    }

    private async Task<bool> WasAttemptedAsync(
        QueuedShoutout item,
        CancellationToken cancellationToken
    )
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        string attemptId = ShoutoutSender.AttemptId(
            ShoutoutSender.DeliveryKey(item.BroadcasterId, item.Target.Id, item.EnqueuedAt)
        );
        return await db.ChannelEvents.AnyAsync(e => e.Id == attemptId, cancellationToken);
    }

    private async Task RunAsync(QueuedShoutout item, CancellationToken cancellationToken)
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
                new(
                    item.BroadcasterId,
                    item.Target,
                    item.Announcement,
                    item.Speak,
                    DeliveryKey: ShoutoutSender.DeliveryKey(
                        item.BroadcasterId,
                        item.Target.Id,
                        item.EnqueuedAt
                    )
                ),
                cancellationToken
            );
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            result = ActionResult.RetryableFailure(ex.Message);
        }

        if (result.Succeeded)
        {
            _nextRetryAt.Remove((item.BroadcasterId, item.Target.Id));
            await _queue.RemoveAsync(item.BroadcasterId, item.Target.Id, cancellationToken);
            return;
        }

        // A rate limit or a dropped connection waits out its window and goes again; only a permanent error
        // (or a run that never succeeds in MaxAttempts passes) drops the item.
        if (result.Retryable)
        {
            _nextRetryAt[(item.BroadcasterId, item.Target.Id)] =
                _timeProvider.GetUtcNow() + item.GlobalCooldown;
            _logger.LogInformation(
                "Queued shoutout to {UserId} hit a temporary error and waits for the next window: {Error}",
                item.Target.Id,
                result.ErrorMessage
            );
            return;
        }
        _nextRetryAt.Remove((item.BroadcasterId, item.Target.Id));
        await _queue.RemoveAsync(item.BroadcasterId, item.Target.Id, cancellationToken);
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

        if (item.ActorUserId is { } actorUserId)
            return OutboundSanction.UserAction(ShoutoutActionKey, actorUserId);

        IApplicationDbContext db = services.GetRequiredService<IApplicationDbContext>();
        Guid? actor = await db
            .Users.AsNoTracking()
            .Where(u => u.TwitchUserId == item.TriggeredByUserId)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return OutboundSanction.UserAction(ShoutoutActionKey, actor);
    }
}
