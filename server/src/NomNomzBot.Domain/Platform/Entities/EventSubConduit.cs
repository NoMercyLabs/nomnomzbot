// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Platform.Entities;

/// <summary>
/// The app-global EventSub conduit (schema §F.8). Platform-level (not tenant-scoped): one conduit per app
/// fans every channel's webhook subscriptions across its shards on the SaaS profile. The conduit transport
/// that provisions and reconciles these is deferred (self-host uses the WebSocket transport); this entity is
/// defined so the registry table set is complete and migration-ready.
/// </summary>
public class EventSubConduit : BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Provider { get; set; } = "twitch";

    /// <summary>Twitch's conduit id. Unique.</summary>
    public string ConduitId { get; set; } = null!;

    public int ShardCount { get; set; }

    /// <summary><c>active</c> | <c>degraded</c> | <c>reprovisioning</c> | <c>revoked</c>.</summary>
    public string Status { get; set; } = "active";

    public DateTime? LastReconciledAt { get; set; }

    public virtual ICollection<EventSubConduitShard> Shards { get; set; } = [];
}
