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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Contracts.Chat;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Stream.AutoShoutout;

/// <summary>
/// Old-bot parity (ShoutoutQueueService, auto lane): a known streamer's first chat of a stream waits in a
/// per-stream pending list, then gets a silent shoutout through the shoutout action once the stream is 10
/// minutes old, 5 minutes passed since the chat, the 8 minute global floor passed and no manual or raid
/// shoutout waits ahead. Inside the per-user cooldown the item is dropped without an announcement. A pending
/// list belongs to one stream (<c>WentLiveAt</c>): it is dropped when the channel goes offline or live again.
/// </summary>
public sealed class AutoShoutoutScheduler : IAutoShoutoutScheduler
{
    private const string ShoutoutActionType = "shoutout";

    private static readonly TimeSpan MinStreamAge = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ChatDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan GlobalFloor = TimeSpan.FromMinutes(8);
    private static readonly TimeSpan PerUserCooldown = TimeSpan.FromMinutes(60);

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, StreamSession> _sessions = [];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IChannelRegistry _registry;
    private readonly IShoutoutQueue _queue;
    private readonly IBotSelfEchoGuard _botGuard;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AutoShoutoutScheduler> _logger;

    public AutoShoutoutScheduler(
        IServiceScopeFactory scopeFactory,
        IChannelRegistry registry,
        IShoutoutQueue queue,
        IBotSelfEchoGuard botGuard,
        TimeProvider timeProvider,
        ILogger<AutoShoutoutScheduler> logger
    )
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _queue = queue;
        _botGuard = botGuard;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task TryEnqueueAsync(
        Guid broadcasterId,
        string twitchUserId,
        string displayName,
        CancellationToken cancellationToken
    )
    {
        ChannelContext? channel = _registry.Get(broadcasterId);
        if (channel is not { IsLive: true, WentLiveAt: DateTimeOffset streamStart })
            return;
        if (twitchUserId == channel.TwitchChannelId)
            return;
        if (AlreadyChecked(broadcasterId, streamStart, twitchUserId))
            return;
        if (
            await _botGuard.ShouldSuppressAsync(
                broadcasterId,
                AuthEnums.Platform.Twitch,
                twitchUserId,
                string.Empty,
                cancellationToken
            )
        )
            return;

        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        if (!await SettingOnAsync(db, broadcasterId, cancellationToken))
            return;
        bool isStreamer = await db
            .Channels.AsNoTracking()
            .AnyAsync(c => c.TwitchChannelId == twitchUserId && c.Enabled, cancellationToken);
        Record(broadcasterId, streamStart, twitchUserId, displayName, isStreamer);
    }

    private bool AlreadyChecked(Guid broadcasterId, DateTimeOffset streamStart, string userId)
    {
        lock (_gate)
            return _sessions.TryGetValue(broadcasterId, out StreamSession? session)
                && session.StreamStart == streamStart
                && session.Seen.Contains(userId);
    }

    public async Task ProcessDueAsync(CancellationToken cancellationToken)
    {
        List<Guid> channels;
        lock (_gate)
            channels = [.. _sessions.Keys];

        foreach (Guid broadcasterId in channels)
            await ProcessChannelAsync(broadcasterId, cancellationToken);
    }

    private void Record(
        Guid broadcasterId,
        DateTimeOffset streamStart,
        string userId,
        string name,
        bool isStreamer
    )
    {
        lock (_gate)
        {
            if (
                !_sessions.TryGetValue(broadcasterId, out StreamSession? session)
                || session.StreamStart != streamStart
            )
            {
                session = new(streamStart);
                _sessions[broadcasterId] = session;
            }
            if (session.Seen.Add(userId) && isStreamer)
                session.Pending.Add(new(userId, name, _timeProvider.GetUtcNow()));
        }
    }

    private async Task ProcessChannelAsync(Guid broadcasterId, CancellationToken cancellationToken)
    {
        ChannelContext? channel = _registry.Get(broadcasterId);
        List<PendingShoutout> pending;
        DateTimeOffset streamStart;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(broadcasterId, out StreamSession? session))
                return;
            if (channel is not { IsLive: true, WentLiveAt: DateTimeOffset start })
            {
                _sessions.Remove(broadcasterId);
                return;
            }
            if (session.StreamStart != start)
            {
                _sessions.Remove(broadcasterId);
                return;
            }
            streamStart = start;
            pending = [.. session.Pending];
        }
        if (pending.Count == 0)
            return;

        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        if (!await SettingOnAsync(db, broadcasterId, cancellationToken))
        {
            Remove(broadcasterId, pending.Select(p => p.TwitchUserId));
            return;
        }

        foreach (PendingShoutout item in pending)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            if (
                now - streamStart < MinStreamAge
                || now - item.EnqueuedAt < ChatDelay
                || ShoutoutCooldowns.GlobalActive(channel, GlobalFloor, now)
                || _queue.Peek(broadcasterId) is not null
            )
                continue;

            Remove(broadcasterId, [item.TwitchUserId]);
            if (ShoutoutCooldowns.PerUserActive(channel, item.TwitchUserId, PerUserCooldown, now))
                continue;

            await RunAsync(scope.ServiceProvider, broadcasterId, item, cancellationToken);
        }
    }

    private void Remove(Guid broadcasterId, IEnumerable<string> userIds)
    {
        lock (_gate)
            if (_sessions.TryGetValue(broadcasterId, out StreamSession? session))
                session.Pending.RemoveAll(p => userIds.Contains(p.TwitchUserId));
    }

    private static Task<bool> SettingOnAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        CancellationToken cancellationToken
    ) =>
        db
            .Channels.AsNoTracking()
            .Where(c => c.Id == broadcasterId)
            .Select(c => c.AutoShoutoutEnabled)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task RunAsync(
        IServiceProvider services,
        Guid broadcasterId,
        PendingShoutout item,
        CancellationToken cancellationToken
    )
    {
        try
        {
            ICommandAction? action = services
                .GetServices<ICommandAction>()
                .FirstOrDefault(a => a.ActionType == ShoutoutActionType);
            if (action is null)
            {
                _logger.LogError("Auto shoutout cannot run: the shoutout action is not registered");
                return;
            }

            PipelineExecutionContext context = new()
            {
                BroadcasterId = broadcasterId,
                TriggeredByUserId = item.TwitchUserId,
                TriggeredByDisplayName = item.DisplayName,
                MessageId = string.Empty,
                RawMessage = string.Empty,
                CancellationToken = cancellationToken,
            };
            ActionDefinition definition = new()
            {
                Type = ShoutoutActionType,
                Parameters = new()
                {
                    ["user_id"] = JsonSerializer.SerializeToElement(item.TwitchUserId),
                    ["cooldown_minutes"] = JsonSerializer.SerializeToElement(
                        (int)PerUserCooldown.TotalMinutes
                    ),
                    ["global_cooldown_minutes"] = JsonSerializer.SerializeToElement(
                        (int)GlobalFloor.TotalMinutes
                    ),
                },
            };

            ActionResult result = await action.ExecuteAsync(context, definition);
            if (!result.Succeeded)
                _logger.LogWarning(
                    "Auto shoutout to {UserId} failed: {Error}",
                    item.TwitchUserId,
                    result.ErrorMessage
                );
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Auto shoutout to {UserId} threw", item.TwitchUserId);
        }
    }

    private sealed class StreamSession(DateTimeOffset streamStart)
    {
        public DateTimeOffset StreamStart { get; } = streamStart;
        public List<PendingShoutout> Pending { get; } = [];
        public HashSet<string> Seen { get; } = [];
    }

    private sealed record PendingShoutout(
        string TwitchUserId,
        string DisplayName,
        DateTimeOffset EnqueuedAt
    );
}
