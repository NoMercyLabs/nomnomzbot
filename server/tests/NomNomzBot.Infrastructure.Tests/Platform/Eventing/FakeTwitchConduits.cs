// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.Security;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// An in-memory model of Twitch's conduit state for one app: conduits, their shards, and which WebSocket
/// session each shard is bound to. Shared between the "instances" of a test the way Twitch is shared between
/// two colours. Every call is recorded in order so a test can assert the exact Helix sequence. Like the real
/// Helix transport, a write with no outbound sanction in force is refused — the production failure mode that
/// kept conduit mode off in the first deploy.
/// </summary>
internal sealed class FakeTwitchConduits : ITwitchEventSubConduitsApi
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, List<ShardState>> _conduits = [];
    private readonly IOutboundSanctionAccessor _sanctions = new OutboundSanctionAccessor();
    private int _nextConduit = 1;

    public List<string> Calls { get; } = [];

    /// <summary>When set, every call fails with this code (models a self-host without an app secret).</summary>
    public string? FailWith { get; set; }

    public IReadOnlyCollection<string> ConduitIds
    {
        get
        {
            lock (_lock)
                return [.. _conduits.Keys];
        }
    }

    /// <summary>Seeds a conduit Twitch already has (e.g. created by a previous boot).</summary>
    public void Seed(string conduitId, int shardCount)
    {
        lock (_lock)
            _conduits[conduitId] =
            [
                .. Enumerable.Range(0, shardCount).Select(i => new ShardState(i.ToString())),
            ];
    }

    /// <summary>Models Twitch dropping a conduit on its own (e.g. 72 h with no enabled shard).</summary>
    public void DropOnTwitch(string conduitId)
    {
        lock (_lock)
            _conduits.Remove(conduitId);
    }

    /// <summary>Models a conduit's shard count drifting outside this deployment (another tool resized it).</summary>
    public void ResizeOnTwitch(string conduitId, int shardCount)
    {
        lock (_lock)
            _conduits[conduitId] =
            [
                .. Enumerable.Range(0, shardCount).Select(i => new ShardState(i.ToString())),
            ];
    }

    /// <summary>Models a shard already bound to a session before the test's instance starts.</summary>
    public void BindOnTwitch(string conduitId, string shardId, string sessionId)
    {
        lock (_lock)
        {
            ShardState shard = _conduits[conduitId].Single(s => s.Id == shardId);
            shard.SessionId = sessionId;
            shard.Status = "enabled";
        }
    }

    /// <summary>Models Twitch noticing a WebSocket closed: the shard bound to it turns disabled.</summary>
    public void Disconnect(string sessionId)
    {
        lock (_lock)
            foreach (ShardState shard in _conduits.Values.SelectMany(s => s))
                if (shard.SessionId == sessionId)
                    shard.Status = "websocket_disconnected";
    }

    public (string Status, string? SessionId) Shard(string conduitId, string shardId)
    {
        lock (_lock)
        {
            ShardState shard = _conduits[conduitId].Single(s => s.Id == shardId);
            return (shard.Status, shard.SessionId);
        }
    }

    public Task<Result<IReadOnlyList<TwitchConduit>>> GetConduitsAsync(
        CancellationToken ct = default
    )
    {
        Record("GET conduits");
        if (FailWith is { } code)
            return Task.FromResult(Result.Failure<IReadOnlyList<TwitchConduit>>("refused", code));
        lock (_lock)
            return Task.FromResult(
                Result.Success<IReadOnlyList<TwitchConduit>>([
                    .. _conduits.Select(c => new TwitchConduit(c.Key, c.Value.Count)),
                ])
            );
    }

    public Task<Result<TwitchConduit>> CreateConduitAsync(
        int shardCount,
        CancellationToken ct = default
    )
    {
        if (_sanctions.Current is null)
            return Task.FromResult(Unsanctioned<TwitchConduit>());
        Record($"POST conduits {shardCount}");
        string id;
        lock (_lock)
            id = $"conduit-{_nextConduit++}";
        Seed(id, shardCount);
        return Task.FromResult(Result.Success(new TwitchConduit(id, shardCount)));
    }

    public Task<Result<TwitchConduit>> UpdateConduitAsync(
        string conduitId,
        int shardCount,
        CancellationToken ct = default
    )
    {
        if (_sanctions.Current is null)
            return Task.FromResult(Unsanctioned<TwitchConduit>());
        Record($"PATCH conduits {conduitId} {shardCount}");
        lock (_lock)
        {
            List<ShardState> shards = _conduits[conduitId];
            while (shards.Count < shardCount)
                shards.Add(new(shards.Count.ToString()));
            if (shards.Count > shardCount)
                shards.RemoveRange(shardCount, shards.Count - shardCount);
        }
        return Task.FromResult(Result.Success(new TwitchConduit(conduitId, shardCount)));
    }

    public Task<Result> DeleteConduitAsync(string conduitId, CancellationToken ct = default)
    {
        if (_sanctions.Current is null)
            return Task.FromResult(Result.Failure("unsanctioned", "UNSANCTIONED_WRITE"));
        Record($"DELETE conduits {conduitId}");
        lock (_lock)
            _conduits.Remove(conduitId);
        return Task.FromResult(Result.Success());
    }

    public Task<Result<IReadOnlyList<TwitchConduitShard>>> GetConduitShardsAsync(
        string conduitId,
        string? status = null,
        CancellationToken ct = default
    )
    {
        Record($"GET shards {conduitId}");
        lock (_lock)
            return Task.FromResult(
                Result.Success<IReadOnlyList<TwitchConduitShard>>([
                    .. _conduits[conduitId]
                        .Where(s => status is null || s.Status == status)
                        .Select(s => new TwitchConduitShard(
                            s.Id,
                            s.Status,
                            new("websocket", SessionId: s.SessionId)
                        )),
                ])
            );
    }

    public Task<Result<TwitchConduitShardUpdateResult>> UpdateConduitShardsAsync(
        string conduitId,
        IReadOnlyList<TwitchConduitShardAssignment> shards,
        CancellationToken ct = default
    )
    {
        if (_sanctions.Current is null)
            return Task.FromResult(Unsanctioned<TwitchConduitShardUpdateResult>());
        Record(
            $"PATCH shards {conduitId} "
                + string.Join(",", shards.Select(s => $"{s.Id}->{s.SessionId}"))
        );
        List<TwitchConduitShard> updated = [];
        List<TwitchConduitShardError> errors = [];
        lock (_lock)
            foreach (TwitchConduitShardAssignment assignment in shards)
            {
                ShardState? shard = _conduits[conduitId].FirstOrDefault(s => s.Id == assignment.Id);
                if (shard is null)
                {
                    errors.Add(new(assignment.Id, "shard not found", "not_found"));
                    continue;
                }
                shard.SessionId = assignment.SessionId;
                shard.Status = "enabled";
                updated.Add(
                    new(shard.Id, shard.Status, new("websocket", SessionId: shard.SessionId))
                );
            }
        return Task.FromResult(Result.Success(new TwitchConduitShardUpdateResult(updated, errors)));
    }

    /// <summary>Adds a non-Helix step (a socket closing, a create) to the same ordered timeline.</summary>
    public void Note(string step) => Record(step);

    /// <summary>How many shards Twitch would deliver to right now.</summary>
    public int EnabledShardCount(string conduitId)
    {
        lock (_lock)
            return _conduits[conduitId].Count(s => s.Status == "enabled");
    }

    private Result<T> Unsanctioned<T>()
    {
        Record("REFUSED unsanctioned write");
        return Result.Failure<T>("unsanctioned", "UNSANCTIONED_WRITE");
    }

    private void Record(string call)
    {
        lock (_lock)
            Calls.Add(call);
    }

    private sealed class ShardState(string id)
    {
        public string Id { get; } = id;
        public string Status { get; set; } = "websocket_disconnected";
        public string? SessionId { get; set; }
    }
}
