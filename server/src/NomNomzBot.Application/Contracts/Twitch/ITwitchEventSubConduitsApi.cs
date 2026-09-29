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
/// The Helix "EventSub conduits" sub-client: every conduit and conduit-shard endpoint. All calls ride the
/// app access token (Twitch requires it for conduits), so they fail with <c>no_token</c> on a self-host
/// without an app secret. See twitch-eventsub.md §10 for how the handover uses them.
/// </summary>
public interface ITwitchEventSubConduitsApi
{
    /// <summary><c>GET /eventsub/conduits</c> — every conduit this app owns.</summary>
    Task<Result<IReadOnlyList<TwitchConduit>>> GetConduitsAsync(CancellationToken ct = default);

    /// <summary><c>POST /eventsub/conduits</c> — a new conduit with <paramref name="shardCount"/> shards.</summary>
    Task<Result<TwitchConduit>> CreateConduitAsync(int shardCount, CancellationToken ct = default);

    /// <summary><c>PATCH /eventsub/conduits</c> — changes a conduit's shard count.</summary>
    Task<Result<TwitchConduit>> UpdateConduitAsync(
        string conduitId,
        int shardCount,
        CancellationToken ct = default
    );

    /// <summary><c>DELETE /eventsub/conduits?id=</c>. Idempotent: an already-deleted conduit is success.</summary>
    Task<Result> DeleteConduitAsync(string conduitId, CancellationToken ct = default);

    /// <summary>
    /// <c>GET /eventsub/conduits/shards</c> — every shard of a conduit (follows the cursor), optionally
    /// filtered by <paramref name="status"/>.
    /// </summary>
    Task<Result<IReadOnlyList<TwitchConduitShard>>> GetConduitShardsAsync(
        string conduitId,
        string? status = null,
        CancellationToken ct = default
    );

    /// <summary><c>PATCH /eventsub/conduits/shards</c> — binds shards to a WebSocket session or a webhook.</summary>
    Task<Result<TwitchConduitShardUpdateResult>> UpdateConduitShardsAsync(
        string conduitId,
        IReadOnlyList<TwitchConduitShardAssignment> shards,
        CancellationToken ct = default
    );
}
