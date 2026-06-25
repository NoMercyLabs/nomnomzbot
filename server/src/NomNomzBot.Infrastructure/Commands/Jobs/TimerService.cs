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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using Timer = NomNomzBot.Domain.Commands.Entities.Timer;

namespace NomNomzBot.Infrastructure.Commands.Jobs;

/// <summary>
/// Background service that manages per-channel message timers.
///
/// Each timer has:
///   - IntervalMinutes: how often to fire
///   - MinChatActivity: minimum new chat messages since last fire before the timer will fire again
///   - Messages: round-robin list of messages to send
///
/// The service polls every minute, checking whether any timer is due.
/// Timer state (last fired, message index) is persisted back to the database.
/// </summary>
public sealed class TimerService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IChannelRegistry _registry;
    private readonly ITemplateResolver _templateResolver;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TimerService> _logger;

    // Per-timer in-memory state: tracks message count at last fire for activity gating
    // Key: timer.Id, Value: MessageCount snapshot when the timer last fired
    private readonly ConcurrentDictionary<Guid, long> _messageCountAtLastFire = new();

    public TimerService(
        IServiceScopeFactory scopeFactory,
        IChannelRegistry registry,
        ITemplateResolver templateResolver,
        TimeProvider timeProvider,
        ILogger<TimerService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _templateResolver = templateResolver;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TimerService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TimerService tick failed");
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        // Only process channels the bot is actively connected to
        IReadOnlyCollection<ChannelContext> liveChannels = _registry.GetAll();
        if (liveChannels.Count == 0)
            return;

        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        IChatProvider chat = scope.ServiceProvider.GetRequiredService<IChatProvider>();

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        List<Guid> broadcasterIds = liveChannels.Select(c => c.BroadcasterId).ToList();

        List<Timer> timers = await db
            .Timers.Where(t =>
                t.IsEnabled && t.DeletedAt == null && broadcasterIds.Contains(t.BroadcasterId)
            )
            .ToListAsync(ct);

        foreach (Timer timer in timers)
        {
            try
            {
                await ProcessTimerAsync(db, chat, timer, now, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Timer {TimerId} ({Name}) failed", timer.Id, timer.Name);
            }
        }
    }

    private async Task ProcessTimerAsync(
        IApplicationDbContext db,
        IChatProvider chat,
        Timer timer,
        DateTime now,
        CancellationToken ct
    )
    {
        // Check interval
        DateTime nextFire = (timer.LastFiredAt ?? DateTime.MinValue).AddMinutes(
            timer.IntervalMinutes
        );
        if (now < nextFire)
            return;

        ChannelContext? channelCtx = _registry.Get(timer.BroadcasterId);
        if (channelCtx is null)
            return;

        // Check minimum chat activity since last fire
        if (timer.MinChatActivity > 0)
        {
            long countAtLastFire = _messageCountAtLastFire.TryGetValue(timer.Id, out long snap)
                ? snap
                : 0L;
            long messagesSinceLastFire = channelCtx.MessageCount - countAtLastFire;
            if (messagesSinceLastFire < timer.MinChatActivity)
            {
                _logger.LogDebug(
                    "Timer {Name} skipped — only {Count}/{Required} messages since last fire",
                    timer.Name,
                    messagesSinceLastFire,
                    timer.MinChatActivity
                );
                return;
            }
        }

        if (timer.Messages.Count == 0)
            return;

        // Pick next message (round-robin)
        string messageTemplate = timer.Messages[timer.NextMessageIndex % timer.Messages.Count];

        // Resolve template variables (template resolution is keyed by the tenant Guid)
        string message = await _templateResolver.ResolveAsync(
            messageTemplate,
            new Dictionary<string, string>(),
            timer.BroadcasterId,
            ct
        );

        if (string.IsNullOrWhiteSpace(message))
            return;

        // IChatProvider takes the tenant Guid and resolves it to the Twitch channel string id internally.
        await chat.SendMessageAsync(channelCtx.BroadcasterId, message, cancellationToken: ct);

        // Persist state: LastFiredAt + NextMessageIndex
        timer.LastFiredAt = now;
        timer.NextMessageIndex = (timer.NextMessageIndex + 1) % timer.Messages.Count;
        await db.SaveChangesAsync(ct);

        // Record message count snapshot for next activity check
        _messageCountAtLastFire[timer.Id] = channelCtx.MessageCount;

        _logger.LogInformation(
            "Timer {Name} fired in channel {BroadcasterId}: \"{Message}\"",
            timer.Name,
            timer.BroadcasterId,
            message
        );
    }
}
