// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Domain.Platform.Entities;

/// <summary>
/// The per-tenant EventSub subscription registry row (schema §F.7, twitch-eventsub §1). One row per desired
/// <c>(BroadcasterId, Provider, EventType, Version)</c>; the lifecycle host reconciles these against Twitch's
/// actual subscription set. Supersedes the legacy <c>EventSubscription</c> (string PK, banned jsonb mappings).
/// Soft-deletable and tenant-scoped, so it inherits the composing tenant + soft-delete global query filter.
/// </summary>
public class EventSubSubscription : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Owning tenant (FK→Channels). The tenant key.</summary>
    public Guid BroadcasterId { get; set; }

    /// <summary>The event provider — <c>twitch</c>.</summary>
    public string Provider { get; set; } = "twitch";

    /// <summary>The EventSub topic, e.g. <c>channel.follow</c>.</summary>
    public string EventType { get; set; } = null!;

    /// <summary>The topic version, e.g. <c>1</c> / <c>2</c>.</summary>
    public string Version { get; set; } = null!;

    /// <summary>The EventSub condition (e.g. <c>broadcaster_user_id</c>), serialized via the hand-rolled JSON converter.</summary>
    public Dictionary<string, string> Condition { get; set; } = new();

    /// <summary>The wire transport — <c>websocket</c> | <c>conduit</c> | <c>webhook</c>.</summary>
    public string Transport { get; set; } = "websocket";

    /// <summary>Twitch's subscription id once created; null while pending.</summary>
    public string? TwitchSubscriptionId { get; set; }

    /// <summary>The WebSocket session id this subscription is homed on (websocket transport).</summary>
    public string? SessionId { get; set; }

    /// <summary>The conduit id this subscription is homed on (conduit transport).</summary>
    public string? ConduitId { get; set; }

    /// <summary>The conduit shard id (conduit transport).</summary>
    public string? ShardId { get; set; }

    /// <summary>Registry status — <c>pending</c> | <c>enabled</c> | <c>failed</c> | <c>revoked</c>.</summary>
    public string Status { get; set; } = "pending";

    /// <summary>Whether this subscription is desired (declarative intent); a disabled row is pruned at Twitch.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Twitch's per-subscription cost (counts against the app's cost budget).</summary>
    public int? Cost { get; set; }

    /// <summary>The last Twitch error body when <see cref="Status"/> is <c>failed</c>.</summary>
    public string? LastError { get; set; }

    /// <summary>When Twitch reports the subscription expires (webhook verification window, etc.).</summary>
    public DateTime? ExpiresAt { get; set; }

    public virtual Channel Channel { get; set; } = null!;
}
