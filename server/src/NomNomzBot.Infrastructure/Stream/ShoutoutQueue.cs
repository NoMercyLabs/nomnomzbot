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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Stream;

/// <summary>
/// Durable shoutout queue with an in-memory order index. A raid goes before every manual shoutout and after
/// raids already waiting; a manual shoutout goes to the end. A target never waits twice.
/// </summary>
public sealed class ShoutoutQueue : IShoutoutQueue
{
    private const string PendingType = "bot.shoutout.pending";
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly SemaphoreSlim _persistenceGate = new(1, 1);
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, List<QueuedShoutout>> _byChannel = [];

    public ShoutoutQueue(IServiceScopeFactory? scopeFactory = null) => _scopeFactory = scopeFactory;

    public async Task<bool> EnqueueAsync(QueuedShoutout item, CancellationToken cancellationToken)
    {
        if (_scopeFactory is null)
            return Enqueue(item);

        await _persistenceGate.WaitAsync(cancellationToken);
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IApplicationDbContext db =
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            string id = PendingId(item.BroadcasterId, item.Target.Id);
            if (await db.ChannelEvents.AnyAsync(e => e.Id == id, cancellationToken))
                return false;

            DateTime createdAt = item.EnqueuedAt.UtcDateTime;
            DateTime? lastQueuedAt = await db
                .ChannelEvents.Where(e =>
                    e.ChannelId == item.BroadcasterId && e.Type == PendingType
                )
                .MaxAsync(e => (DateTime?)e.CreatedAt, cancellationToken);
            if (lastQueuedAt >= createdAt)
                createdAt = lastQueuedAt.Value.AddTicks(1);

            db.ChannelEvents.Add(
                new ChannelEvent
                {
                    Id = id,
                    ChannelId = item.BroadcasterId,
                    Type = PendingType,
                    Data = JsonSerializer.Serialize(item),
                    CreatedAt = createdAt,
                }
            );
            await db.SaveChangesAsync(cancellationToken);
            Enqueue(item);
            return true;
        }
        finally
        {
            _persistenceGate.Release();
        }
    }

    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        if (_scopeFactory is null)
            return;

        await _persistenceGate.WaitAsync(cancellationToken);
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IApplicationDbContext db =
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            List<string> items = await db
                .ChannelEvents.AsNoTracking()
                .Where(e => e.Type == PendingType && e.Data != null)
                .OrderBy(e => e.CreatedAt)
                .Select(e => e.Data!)
                .ToListAsync(cancellationToken);
            foreach (string data in items)
            {
                QueuedShoutout? item = JsonSerializer.Deserialize<QueuedShoutout>(data);
                if (item is not null)
                    Enqueue(item);
            }
        }
        finally
        {
            _persistenceGate.Release();
        }
    }

    public async Task<bool> RemoveAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        CancellationToken cancellationToken
    )
    {
        if (_scopeFactory is null)
            return Remove(broadcasterId, targetTwitchUserId);

        await _persistenceGate.WaitAsync(cancellationToken);
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IApplicationDbContext db =
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            ChannelEvent? pending = await db.ChannelEvents.FindAsync(
                [PendingId(broadcasterId, targetTwitchUserId)],
                cancellationToken
            );
            if (pending is null)
                return false;
            db.ChannelEvents.Remove(pending);
            await db.SaveChangesAsync(cancellationToken);
            Remove(broadcasterId, targetTwitchUserId);
            return true;
        }
        finally
        {
            _persistenceGate.Release();
        }
    }

    public async Task ClearAsync(Guid broadcasterId, CancellationToken cancellationToken)
    {
        if (_scopeFactory is not null)
        {
            await _persistenceGate.WaitAsync(cancellationToken);
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IApplicationDbContext db =
                    scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                List<ChannelEvent> pending = await db
                    .ChannelEvents.Where(e => e.ChannelId == broadcasterId && e.Type == PendingType)
                    .ToListAsync(cancellationToken);
                db.ChannelEvents.RemoveRange(pending);
                await db.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                _persistenceGate.Release();
            }
        }
        Clear(broadcasterId);
    }

    private static string PendingId(Guid broadcasterId, string targetTwitchUserId)
    {
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{broadcasterId:N}:{targetTwitchUserId}")
        );
        return "so-" + Convert.ToHexString(hash.AsSpan(0, 16));
    }

    public bool Enqueue(QueuedShoutout item)
    {
        lock (_gate)
        {
            if (!_byChannel.TryGetValue(item.BroadcasterId, out List<QueuedShoutout>? list))
            {
                list = [];
                _byChannel[item.BroadcasterId] = list;
            }
            if (list.Exists(q => q.Target.Id == item.Target.Id))
                return false;

            int index = item.IsRaid ? list.FindLastIndex(q => q.IsRaid) + 1 : list.Count;
            list.Insert(index, item);
            return true;
        }
    }

    public QueuedShoutout? Peek(Guid broadcasterId)
    {
        lock (_gate)
            return
                _byChannel.TryGetValue(broadcasterId, out List<QueuedShoutout>? list)
                && list.Count > 0
                ? list[0]
                : null;
    }

    public bool Remove(Guid broadcasterId, string targetTwitchUserId)
    {
        lock (_gate)
        {
            if (!_byChannel.TryGetValue(broadcasterId, out List<QueuedShoutout>? list))
                return false;
            int removed = list.RemoveAll(q => q.Target.Id == targetTwitchUserId);
            if (list.Count == 0)
                _byChannel.Remove(broadcasterId);
            return removed > 0;
        }
    }

    public void Clear(Guid broadcasterId)
    {
        lock (_gate)
            _byChannel.Remove(broadcasterId);
    }

    public IReadOnlyList<Guid> ChannelsWithPending()
    {
        lock (_gate)
            return _byChannel.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key).ToList();
    }
}
