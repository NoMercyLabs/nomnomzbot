// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

// Helix "EventSub conduits" wire models (GET/POST/PATCH/DELETE /eventsub/conduits and
// GET/PATCH /eventsub/conduits/shards). They deserialize straight from Twitch's snake_case JSON via the
// transport's naming policy — no per-property annotations. Conduits are app-level: every call rides the
// app access token, so no tenant id appears here.

/// <summary>One conduit of this app: its id and how many shards it has.</summary>
public sealed record TwitchConduit(string Id, int ShardCount);

/// <summary>
/// One shard of a conduit. <see cref="Status"/> is Twitch's value verbatim: <c>enabled</c>,
/// <c>webhook_callback_verification_pending</c>, <c>webhook_callback_verification_failed</c>,
/// <c>notification_failures_exceeded</c>, <c>websocket_disconnected</c>, <c>websocket_failed_ping_pong</c>,
/// <c>websocket_received_inbound_traffic</c>, <c>websocket_internal_error</c>,
/// <c>websocket_network_timeout</c>, <c>websocket_network_error</c>, <c>websocket_failed_to_reconnect</c>.
/// </summary>
public sealed record TwitchConduitShard(
    string Id,
    string Status,
    TwitchConduitShardTransport? Transport
)
{
    /// <summary>The only status in which Twitch delivers notifications to this shard.</summary>
    public bool IsEnabled => string.Equals(Status, "enabled", StringComparison.Ordinal);
}

/// <summary>The transport a shard is bound to: a webhook callback or a WebSocket session.</summary>
public sealed record TwitchConduitShardTransport(
    string Method,
    string? Callback = null,
    string? SessionId = null,
    DateTimeOffset? ConnectedAt = null,
    DateTimeOffset? DisconnectedAt = null
);

/// <summary>
/// One shard assignment for Update Conduit Shards. Build it with <see cref="ForWebSocket"/> or
/// <see cref="ForWebhook"/>: Twitch rejects a body that mixes both transports.
/// </summary>
public sealed record TwitchConduitShardAssignment(
    string Id,
    string Method,
    string? SessionId = null,
    string? Callback = null,
    string? Secret = null
)
{
    public static TwitchConduitShardAssignment ForWebSocket(string shardId, string sessionId) =>
        new(shardId, "websocket", SessionId: sessionId);

    public static TwitchConduitShardAssignment ForWebhook(
        string shardId,
        string callback,
        string secret
    ) => new(shardId, "webhook", Callback: callback, Secret: secret);
}

/// <summary>A shard Twitch refused to update, with its reason.</summary>
public sealed record TwitchConduitShardError(string Id, string Message, string Code);

/// <summary>
/// The result of Update Conduit Shards. The call itself succeeds even when single shards are refused, so the
/// refusals come back in <see cref="Errors"/> rather than as a failed result.
/// </summary>
public sealed record TwitchConduitShardUpdateResult(
    IReadOnlyList<TwitchConduitShard> Shards,
    IReadOnlyList<TwitchConduitShardError> Errors
);
