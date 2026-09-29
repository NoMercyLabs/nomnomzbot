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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// The conduit + shard owner (twitch-eventsub.md §10). A singleton: the conduit id and the claimed shard are
/// process state, while every Helix and database call runs in its own short scope.
/// <para>
/// Invariant it protects: the conduit has exactly two shards and each running instance holds exactly one.
/// With two shards, a notification Twitch routes to a disabled shard is resent to the other one, so a single
/// disabled shard never loses an event.
/// </para>
/// </summary>
public sealed class EventSubConduitShardCoordinator(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOutboundSanctionAccessor sanctions,
    ILogger<EventSubConduitShardCoordinator> logger
) : IEventSubConduitShardCoordinator
{
    private const string Provider = "twitch";

    // The conduit and its shards are the deployment's own delivery plumbing, not a broadcaster's state: the
    // same platform-level basis the per-owner WebSocket transport claims for its subscription lifecycle.
    private static readonly OutboundSanction ConduitLifecycle =
        OutboundSanction.PlatformConfiguration("eventsub_conduit_lifecycle");

    // Two instances booting at once must not both create a conduit: the loser would persist a second one and
    // split the subscriptions. Only the create path takes this lease; reusing the persisted conduit does not.
    private const string ProvisionLease = "eventsub-conduit-provision";
    private static readonly TimeSpan ProvisionLeaseTtl = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ProvisionRetryDelay = TimeSpan.FromMilliseconds(500);
    private const int ProvisionAttempts = 20;

    private static readonly TimeSpan SuccessorPollInterval = TimeSpan.FromMilliseconds(500);

    private readonly SemaphoreSlim _ensureGate = new(1, 1);
    private volatile string? _conduitId;
    private Guid _conduitRowId;
    private volatile string? _claimedShardId;

    public string? ConduitId => _conduitId;

    public string? ClaimedShardId => _claimedShardId;

    public async Task<Result<string>> EnsureConduitAsync(CancellationToken ct = default)
    {
        using IDisposable sanction = sanctions.Begin(ConduitLifecycle);
        if (_conduitId is { } cached)
            return Result.Success(cached);

        await _ensureGate.WaitAsync(ct);
        try
        {
            if (_conduitId is { } raced)
                return Result.Success(raced);

            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            ITwitchEventSubConduitsApi api =
                scope.ServiceProvider.GetRequiredService<ITwitchEventSubConduitsApi>();
            IApplicationDbContext db =
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            Result<IReadOnlyList<TwitchConduit>> listed = await api.GetConduitsAsync(ct);
            if (listed.IsFailure)
                return listed.WithValue<string>(default!);

            Result<string> reused = await TryReuseAsync(api, db, listed.Value, ct);
            if (reused.IsSuccess || reused.ErrorCode != TwitchErrorCodes.NotFound)
                return reused;

            return await CreateUnderLeaseAsync(scope.ServiceProvider, api, db, ct);
        }
        finally
        {
            _ensureGate.Release();
        }
    }

    /// <summary>
    /// Reuses the persisted conduit when Twitch still lists it. An unpersisted conduit is never adopted: on a
    /// shared client id it belongs to another deployment, and taking its shards would steal its events.
    /// </summary>
    private async Task<Result<string>> TryReuseAsync(
        ITwitchEventSubConduitsApi api,
        IApplicationDbContext db,
        IReadOnlyList<TwitchConduit> listed,
        CancellationToken ct
    )
    {
        EventSubConduit? row = await db.EventSubConduits.FirstOrDefaultAsync(
            c => c.Provider == Provider,
            ct
        );
        TwitchConduit? live = row is null
            ? null
            : listed.FirstOrDefault(c => c.Id == row.ConduitId);
        if (row is null || live is null)
            return Result.Failure<string>(
                "No persisted conduit is live.",
                TwitchErrorCodes.NotFound
            );

        if (live.ShardCount != IEventSubConduitShardCoordinator.ShardCount)
        {
            Result<TwitchConduit> resized = await api.UpdateConduitAsync(
                live.Id,
                IEventSubConduitShardCoordinator.ShardCount,
                ct
            );
            if (resized.IsFailure)
                return resized.WithValue<string>(default!);
        }

        row.ShardCount = IEventSubConduitShardCoordinator.ShardCount;
        row.Status = "active";
        row.LastReconciledAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        Remember(row);
        return Result.Success(row.ConduitId);
    }

    private async Task<Result<string>> CreateUnderLeaseAsync(
        IServiceProvider services,
        ITwitchEventSubConduitsApi api,
        IApplicationDbContext db,
        CancellationToken ct
    )
    {
        IRunOnceGuard guard = services.GetRequiredService<IRunOnceGuard>();
        IAsyncDisposable? lease = null;
        for (int attempt = 0; attempt < ProvisionAttempts && lease is null; attempt++)
        {
            lease = await guard.TryAcquireAsync(ProvisionLease, ProvisionLeaseTtl, ct);
            if (lease is null)
                await Task.Delay(ProvisionRetryDelay, clock, ct);
        }

        if (lease is null)
            return Result.Failure<string>(
                "Another instance is provisioning the EventSub conduit.",
                "SERVICE_UNAVAILABLE"
            );

        await using (lease)
        {
            // The other instance may have finished while we waited for the lease.
            Result<IReadOnlyList<TwitchConduit>> relisted = await api.GetConduitsAsync(ct);
            if (relisted.IsFailure)
                return relisted.WithValue<string>(default!);
            Result<string> reused = await TryReuseAsync(api, db, relisted.Value, ct);
            if (reused.IsSuccess || reused.ErrorCode != TwitchErrorCodes.NotFound)
                return reused;

            Result<TwitchConduit> created = await api.CreateConduitAsync(
                IEventSubConduitShardCoordinator.ShardCount,
                ct
            );
            if (created.IsFailure)
                return created.WithValue<string>(default!);

            EventSubConduit row = await PersistNewConduitAsync(db, created.Value, ct);
            logger.LogInformation(
                "EventSub: created conduit {ConduitId} with {Shards} shards",
                row.ConduitId,
                row.ShardCount
            );
            Remember(row);
            return Result.Success(row.ConduitId);
        }
    }

    /// <summary>
    /// Points the deployment's single conduit row at the new conduit. A replaced conduit (a new client id, or
    /// one Twitch deleted after 72 h without an enabled shard) takes its shard rows with it.
    /// </summary>
    private async Task<EventSubConduit> PersistNewConduitAsync(
        IApplicationDbContext db,
        TwitchConduit created,
        CancellationToken ct
    )
    {
        EventSubConduit? row = await db.EventSubConduits.FirstOrDefaultAsync(
            c => c.Provider == Provider,
            ct
        );
        if (row is null)
        {
            row = new() { Provider = Provider, ConduitId = created.Id };
            await db.EventSubConduits.AddAsync(row, ct);
        }
        else
        {
            List<EventSubConduitShard> staleShards = await db
                .EventSubConduitShards.Where(s => s.ConduitId == row.Id)
                .ToListAsync(ct);
            db.EventSubConduitShards.RemoveRange(staleShards);
            row.ConduitId = created.Id;
        }

        row.ShardCount = created.ShardCount;
        row.Status = "active";
        row.LastReconciledAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return row;
    }

    private void Remember(EventSubConduit row)
    {
        _conduitRowId = row.Id;
        _conduitId = row.ConduitId;
    }

    public async Task<Result<string>> ClaimShardAsync(
        string sessionId,
        CancellationToken ct = default
    )
    {
        using IDisposable sanction = sanctions.Begin(ConduitLifecycle);
        Result<string> conduit = await EnsureConduitAsync(ct);
        if (conduit.IsFailure)
            return conduit;

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        ITwitchEventSubConduitsApi api =
            scope.ServiceProvider.GetRequiredService<ITwitchEventSubConduitsApi>();

        Result<IReadOnlyList<TwitchConduitShard>> shards = await api.GetConduitShardsAsync(
            conduit.Value,
            ct: ct
        );
        if (shards.IsFailure)
            return shards.WithValue<string>(default!);

        // A session_reconnect keeps the shard on the session it replaces; re-binding the same shard to the new
        // session id is idempotent, so the shard this process already holds is always tried first.
        IEnumerable<TwitchConduitShard> candidates = shards
            .Value.Where(s => !s.IsEnabled || s.Id == _claimedShardId)
            .OrderByDescending(s => s.Id == _claimedShardId)
            .ThenBy(s => s.Id, StringComparer.Ordinal);

        foreach (TwitchConduitShard candidate in candidates)
        {
            Result<bool> bound = await TryBindAsync(
                api,
                conduit.Value,
                candidate.Id,
                sessionId,
                ct
            );
            if (bound.IsFailure)
                return bound.WithValue<string>(default!);
            if (!bound.Value)
                continue;

            _claimedShardId = candidate.Id;
            await PersistClaimAsync(scope.ServiceProvider, candidate.Id, sessionId, ct);
            logger.LogInformation(
                "EventSub: shard {ShardId} of conduit {ConduitId} bound to session {SessionId}",
                candidate.Id,
                conduit.Value,
                sessionId
            );
            return Result.Success(candidate.Id);
        }

        return Result.Failure<string>(
            "Every conduit shard is enabled on another session.",
            "SERVICE_UNAVAILABLE"
        );
    }

    /// <summary>
    /// PATCHes one shard onto the session, then re-reads it: two instances binding the same free shard at the
    /// same moment both get a clean PATCH, and only the re-read tells which one Twitch kept.
    /// </summary>
    private static async Task<Result<bool>> TryBindAsync(
        ITwitchEventSubConduitsApi api,
        string conduitId,
        string shardId,
        string sessionId,
        CancellationToken ct
    )
    {
        Result<TwitchConduitShardUpdateResult> updated = await api.UpdateConduitShardsAsync(
            conduitId,
            [TwitchConduitShardAssignment.ForWebSocket(shardId, sessionId)],
            ct
        );
        if (updated.IsFailure)
            return updated.WithValue<bool>(default);
        if (updated.Value.Errors.Any(e => e.Id == shardId))
            return Result.Success(false);

        Result<IReadOnlyList<TwitchConduitShard>> confirmed = await api.GetConduitShardsAsync(
            conduitId,
            ct: ct
        );
        if (confirmed.IsFailure)
            return confirmed.WithValue<bool>(default);

        return Result.Success(
            confirmed.Value.Any(s => s.Id == shardId && s.Transport?.SessionId == sessionId)
        );
    }

    private async Task PersistClaimAsync(
        IServiceProvider services,
        string shardId,
        string sessionId,
        CancellationToken ct
    )
    {
        IApplicationDbContext db = services.GetRequiredService<IApplicationDbContext>();
        EventSubConduitShard? row = await db.EventSubConduitShards.FirstOrDefaultAsync(
            s => s.ConduitId == _conduitRowId && s.ShardId == shardId,
            ct
        );
        if (row is null)
        {
            row = new()
            {
                ConduitId = _conduitRowId,
                ShardId = shardId,
                Transport = "websocket",
            };
            await db.EventSubConduitShards.AddAsync(row, ct);
        }

        row.SessionId = sessionId;
        row.Status = "enabled";
        row.AssignedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> WaitForSuccessorShardAsync(
        TimeSpan timeout,
        CancellationToken ct = default
    )
    {
        if (_conduitId is not { } conduitId)
            return false;

        using CancellationTokenSource budget = new(timeout, clock);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            ct,
            budget.Token
        );
        try
        {
            while (true)
            {
                if (await IsAnotherShardEnabledAsync(conduitId, linked.Token))
                    return true;
                await Task.Delay(SuccessorPollInterval, clock, linked.Token);
            }
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested)
        {
            logger.LogWarning(
                "EventSub: no successor shard came up within {Timeout:g}; closing anyway",
                timeout
            );
            return false;
        }
    }

    private async Task<bool> IsAnotherShardEnabledAsync(string conduitId, CancellationToken ct)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        ITwitchEventSubConduitsApi api =
            scope.ServiceProvider.GetRequiredService<ITwitchEventSubConduitsApi>();
        Result<IReadOnlyList<TwitchConduitShard>> shards = await api.GetConduitShardsAsync(
            conduitId,
            ct: ct
        );
        return shards.IsSuccess && shards.Value.Any(s => s.IsEnabled && s.Id != _claimedShardId);
    }

    public void ReleaseClaim() => _claimedShardId = null;
}
