// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Contracts.Chat;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using ChannelEvent = NomNomzBot.Domain.Identity.Entities.ChannelEvent;

namespace NomNomzBot.Infrastructure.Stream.AutoShoutout;

/// <summary>
/// Old-bot parity (ShoutoutQueueService, auto lane): a known streamer's first chat of a stream waits in a
/// per-stream pending list, then gets a silent shoutout through the shoutout action once the stream is 10
/// minutes old, 5 minutes passed since the chat, the 8 minute global floor passed and no manual or raid
/// shoutout waits ahead. Inside the per-user cooldown the item continues waiting. A pending
/// list belongs to one stream (<c>WentLiveAt</c>): it is dropped when the channel goes offline or live again.
/// </summary>
public sealed class AutoShoutoutScheduler : IAutoShoutoutScheduler
{
    private const string ShoutoutActionType = "shoutout";
    private const string AutoShoutoutSanction = "channel:auto-shoutout";
    private const string PendingType = "bot.shoutout.auto-pending";

    private static readonly TimeSpan MinStreamAge = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ChatDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan GlobalFloor = TimeSpan.FromMinutes(8);
    private static readonly TimeSpan PerUserCooldown = TimeSpan.FromMinutes(60);

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, StreamSession> _sessions = [];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IChannelRegistry _registry;
    private readonly IShoutoutQueue _queue;
    private readonly IOutboundSanctionAccessor _sanctions;
    private readonly IBotSelfEchoGuard _botGuard;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AutoShoutoutScheduler> _logger;

    public AutoShoutoutScheduler(
        IServiceScopeFactory scopeFactory,
        IChannelRegistry registry,
        IShoutoutQueue queue,
        IOutboundSanctionAccessor sanctions,
        IBotSelfEchoGuard botGuard,
        TimeProvider timeProvider,
        ILogger<AutoShoutoutScheduler> logger
    )
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _queue = queue;
        _sanctions = sanctions;
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
        DateTimeOffset enqueuedAt = _timeProvider.GetUtcNow();
        if (
            isStreamer
            && !await PersistAsync(
                db,
                broadcasterId,
                streamStart,
                twitchUserId,
                displayName,
                enqueuedAt,
                cancellationToken
            )
        )
            return;
        Record(broadcasterId, streamStart, twitchUserId, displayName, enqueuedAt, isStreamer);
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
        await RecoverAsync(cancellationToken);
        List<Guid> channels;
        lock (_gate)
            channels = [.. _sessions.Keys];

        foreach (Guid broadcasterId in channels)
            await ProcessChannelAsync(broadcasterId, cancellationToken);
    }

    private static async Task<bool> PersistAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        DateTimeOffset streamStart,
        string userId,
        string displayName,
        DateTimeOffset enqueuedAt,
        CancellationToken cancellationToken
    )
    {
        string id = PendingId(broadcasterId, streamStart, userId);
        if (await db.ChannelEvents.AnyAsync(e => e.Id == id, cancellationToken))
            return false;
        DateTime createdAt = enqueuedAt.UtcDateTime;
        DateTime? lastQueuedAt = await db
            .ChannelEvents.Where(e => e.ChannelId == broadcasterId && e.Type == PendingType)
            .MaxAsync(e => (DateTime?)e.CreatedAt, cancellationToken);
        if (lastQueuedAt >= createdAt)
            createdAt = lastQueuedAt.Value.AddTicks(1);
        db.ChannelEvents.Add(
            new ChannelEvent
            {
                Id = id,
                ChannelId = broadcasterId,
                Type = PendingType,
                Data = JsonSerializer.Serialize(
                    new StoredPendingShoutout(
                        broadcasterId,
                        streamStart,
                        userId,
                        displayName,
                        enqueuedAt
                    )
                ),
                CreatedAt = createdAt,
            }
        );
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        List<ChannelEvent> pending = await db
            .ChannelEvents.Where(e => e.Type == PendingType && e.Data != null)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
        foreach (ChannelEvent row in pending)
        {
            StoredPendingShoutout? item = JsonSerializer.Deserialize<StoredPendingShoutout>(
                row.Data!
            );
            ChannelContext? channel = item is null ? null : _registry.Get(item.BroadcasterId);
            if (
                item is not null
                && channel is { IsLive: true, WentLiveAt: DateTimeOffset start }
                && start == item.StreamStart
            )
                Record(
                    item.BroadcasterId,
                    item.StreamStart,
                    item.UserId,
                    item.DisplayName,
                    item.EnqueuedAt,
                    true
                );
            else
                db.ChannelEvents.Remove(row);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string PendingId(Guid broadcasterId, DateTimeOffset streamStart, string userId)
    {
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{broadcasterId:N}:{streamStart.UtcTicks}:{userId}")
        );
        return "sa-" + Convert.ToHexString(hash.AsSpan(0, 16));
    }

    private void Record(
        Guid broadcasterId,
        DateTimeOffset streamStart,
        string userId,
        string name,
        DateTimeOffset enqueuedAt,
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
                session.Pending.Add(new(userId, name, enqueuedAt));
        }
    }

    private async Task ProcessChannelAsync(Guid broadcasterId, CancellationToken cancellationToken)
    {
        ChannelContext? channel = _registry.Get(broadcasterId);
        List<PendingShoutout> pending;
        DateTimeOffset streamStart;
        bool stale;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(broadcasterId, out StreamSession? session))
                return;
            stale =
                channel is not { IsLive: true, WentLiveAt: DateTimeOffset start }
                || session.StreamStart != start;
            streamStart = session.StreamStart;
            pending = [.. session.Pending];
        }
        if (stale)
        {
            await ClearAsync(broadcasterId, cancellationToken);
            return;
        }
        if (pending.Count == 0)
            return;

        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        if (!await SettingOnAsync(db, broadcasterId, cancellationToken))
        {
            await RemoveAsync(
                db,
                broadcasterId,
                streamStart,
                pending.Select(p => p.TwitchUserId),
                cancellationToken
            );
            return;
        }

        foreach (PendingShoutout item in pending)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            if (
                now - streamStart < MinStreamAge
                || now - item.EnqueuedAt < ChatDelay
                || ShoutoutCooldowns.GlobalActive(channel, GlobalFloor, now)
                || ShoutoutCooldowns.PerUserActive(channel, item.TwitchUserId, PerUserCooldown, now)
                || _queue.Peek(broadcasterId) is not null
            )
                continue;

            if (
                await WasShoutedOutThisStreamAsync(
                    db,
                    broadcasterId,
                    streamStart,
                    item.TwitchUserId,
                    cancellationToken
                )
            )
            {
                await RemoveAsync(
                    db,
                    broadcasterId,
                    streamStart,
                    [item.TwitchUserId],
                    cancellationToken
                );
                continue;
            }

            if (await RunAsync(scope.ServiceProvider, broadcasterId, item, cancellationToken))
                await RemoveAsync(
                    db,
                    broadcasterId,
                    streamStart,
                    [item.TwitchUserId],
                    cancellationToken
                );
        }
    }

    private static async Task<bool> WasShoutedOutThisStreamAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        DateTimeOffset streamStart,
        string twitchUserId,
        CancellationToken cancellationToken
    )
    {
        List<string> data = await db
            .ChannelEvents.AsNoTracking()
            .Where(e =>
                e.ChannelId == broadcasterId
                && e.CreatedAt >= streamStart.UtcDateTime
                && (e.Type == "channel.shoutout.create" || e.Type == "bot.shoutout.sent")
                && e.Data != null
            )
            .Select(e => e.Data!)
            .ToListAsync(cancellationToken);

        foreach (string payload in data)
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("toUserId", out JsonElement target)
                && target.ValueKind == JsonValueKind.String
                && target.GetString() == twitchUserId
            )
                return true;
        }

        return false;
    }

    private async Task RemoveAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        DateTimeOffset streamStart,
        IEnumerable<string> userIds,
        CancellationToken cancellationToken
    )
    {
        List<string> ids = [.. userIds];
        foreach (string userId in ids)
        {
            ChannelEvent? pending = await db.ChannelEvents.FindAsync(
                [PendingId(broadcasterId, streamStart, userId)],
                cancellationToken
            );
            if (pending is not null)
                db.ChannelEvents.Remove(pending);
        }
        await db.SaveChangesAsync(cancellationToken);
        lock (_gate)
            if (_sessions.TryGetValue(broadcasterId, out StreamSession? session))
                session.Pending.RemoveAll(p => ids.Contains(p.TwitchUserId));
    }

    private async Task ClearAsync(Guid broadcasterId, CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        List<ChannelEvent> pending = await db
            .ChannelEvents.Where(e => e.ChannelId == broadcasterId && e.Type == PendingType)
            .ToListAsync(cancellationToken);
        db.ChannelEvents.RemoveRange(pending);
        await db.SaveChangesAsync(cancellationToken);
        lock (_gate)
            _sessions.Remove(broadcasterId);
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

    private async Task<bool> RunAsync(
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
                return false;
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

            // No chat message or request stands behind an auto shoutout; the channel's own saved
            // auto-shoutout setting is the basis, and it is what a broadcaster is pointed to.
            using IDisposable sanction = _sanctions.Begin(
                OutboundSanction.ChannelConfiguration(AutoShoutoutSanction)
            );
            ActionResult result = await action.ExecuteAsync(context, definition);
            if (!result.Succeeded)
                _logger.LogWarning(
                    "Auto shoutout to {UserId} failed: {Error}",
                    item.TwitchUserId,
                    result.ErrorMessage
                );
            return result.Succeeded;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Auto shoutout to {UserId} threw", item.TwitchUserId);
            return false;
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

    private sealed record StoredPendingShoutout(
        Guid BroadcasterId,
        DateTimeOffset StreamStart,
        string UserId,
        string DisplayName,
        DateTimeOffset EnqueuedAt
    );
}
