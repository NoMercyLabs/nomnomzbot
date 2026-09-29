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

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// Owns this deployment's EventSub conduit and the one shard this instance holds (twitch-eventsub.md §10).
/// The conduit carries every subscription, so the subscriptions outlive every WebSocket session; each running
/// instance binds one shard to its own session, which is what lets a blue/green deploy hand over without a gap.
/// </summary>
public interface IEventSubConduitShardCoordinator
{
    /// <summary>The number of shards the conduit is kept at: one per colour of a blue/green pair.</summary>
    const int ShardCount = 2;

    /// <summary>The conduit id once <see cref="EnsureConduitAsync"/> has succeeded, else null.</summary>
    string? ConduitId { get; }

    /// <summary>The shard this instance holds, or null while it holds none.</summary>
    string? ClaimedShardId { get; }

    /// <summary>
    /// Returns this deployment's conduit id: the persisted one when Twitch still has it, else a newly created
    /// conduit that replaces it. Fails with <c>no_token</c> when no app access token can be minted — the
    /// caller then stays on per-owner WebSocket sessions.
    /// </summary>
    Task<Result<string>> EnsureConduitAsync(CancellationToken ct = default);

    /// <summary>
    /// Binds a shard that is not enabled to <paramref name="sessionId"/> and confirms Twitch now reports that
    /// session on it. Returns the shard id. Must run within 10 s of the session welcome.
    /// </summary>
    Task<Result<string>> ClaimShardAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Polls the conduit until a shard other than ours is enabled — a successor is receiving — or
    /// <paramref name="timeout"/> passes. True when a successor shard is live.
    /// </summary>
    Task<bool> WaitForSuccessorShardAsync(TimeSpan timeout, CancellationToken ct = default);

    /// <summary>Forgets the claimed shard once this instance's session is closed.</summary>
    void ReleaseClaim();
}
