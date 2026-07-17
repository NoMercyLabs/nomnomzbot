// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Domain.Obs.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Obs.Bridge;

/// <summary>
/// Cache-backed bridge book (obs-control.md §3.3/D2): the entry list per channel lives in
/// <see cref="ICacheService"/> (Redis on SaaS → every node elects identically); the leader is the
/// longest-lived connection, so a newly-opened second OBS never steals execution mid-stream. Every
/// join/leave publishes <see cref="ObsBridgeStateChangedEvent"/> for the dashboard indicator.
/// </summary>
public sealed class ObsBridgeRegistry : IObsBridgeRegistry
{
    private static readonly TimeSpan EntryTtl = TimeSpan.FromHours(12);

    private readonly ICacheService _cache;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _clock;

    public ObsBridgeRegistry(ICacheService cache, IEventBus eventBus, TimeProvider clock)
    {
        _cache = cache;
        _eventBus = eventBus;
        _clock = clock;
    }

    public sealed record BridgeEntry(string ConnectionId, DateTime ConnectedAt);

    public async Task RegisterAsync(
        Guid broadcasterId,
        string connectionId,
        DateTime connectedAt,
        CancellationToken ct = default
    )
    {
        List<BridgeEntry> entries = await LoadAsync(broadcasterId, ct);
        entries.RemoveAll(e => e.ConnectionId == connectionId);
        entries.Add(new BridgeEntry(connectionId, connectedAt));
        await SaveAsync(broadcasterId, entries, ct);
        await PublishStateAsync(broadcasterId, entries, ct);
    }

    public async Task UnregisterAsync(
        Guid broadcasterId,
        string connectionId,
        CancellationToken ct = default
    )
    {
        List<BridgeEntry> entries = await LoadAsync(broadcasterId, ct);
        if (entries.RemoveAll(e => e.ConnectionId == connectionId) == 0)
            return;
        await SaveAsync(broadcasterId, entries, ct);
        await PublishStateAsync(broadcasterId, entries, ct);
    }

    public async Task<string?> GetLeaderAsync(Guid broadcasterId, CancellationToken ct = default)
    {
        List<BridgeEntry> entries = await LoadAsync(broadcasterId, ct);
        return entries.OrderBy(e => e.ConnectedAt).FirstOrDefault()?.ConnectionId;
    }

    public async Task<ObsBridgeStatusDto> GetStatusAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        List<BridgeEntry> entries = await LoadAsync(broadcasterId, ct);
        BridgeEntry? leader = entries.OrderBy(e => e.ConnectedAt).FirstOrDefault();
        return new ObsBridgeStatusDto(entries.Count, leader is not null, leader?.ConnectedAt);
    }

    private async Task<List<BridgeEntry>> LoadAsync(Guid broadcasterId, CancellationToken ct) =>
        await _cache.GetAsync<List<BridgeEntry>>(Key(broadcasterId), ct) ?? [];

    private Task SaveAsync(Guid broadcasterId, List<BridgeEntry> entries, CancellationToken ct) =>
        entries.Count == 0
            ? _cache.RemoveAsync(Key(broadcasterId), ct)
            : _cache.SetAsync(Key(broadcasterId), entries, EntryTtl, ct);

    private async Task PublishStateAsync(
        Guid broadcasterId,
        List<BridgeEntry> entries,
        CancellationToken ct
    ) =>
        await _eventBus.PublishAsync(
            new ObsBridgeStateChangedEvent
            {
                BroadcasterId = broadcasterId,
                OccurredAt = _clock.GetUtcNow(),
                InstanceCount = entries.Count,
                HasLeader = entries.Count > 0,
            },
            ct
        );

    private static string Key(Guid broadcasterId) => $"obs:bridges:{broadcasterId}";
}
