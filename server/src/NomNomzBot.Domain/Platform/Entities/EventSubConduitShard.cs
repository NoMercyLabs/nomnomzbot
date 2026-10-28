// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Platform.Entities;

/// <summary>
/// One shard of an app-global EventSub conduit (schema §F.9). Platform-level. The conduit transport that
/// assigns shards to webhook callbacks / WebSocket sessions is deferred (see <see cref="EventSubConduit"/>);
/// defined for table-set completeness and migration readiness.
/// </summary>
public class EventSubConduitShard : BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Owning conduit (FK→EventSubConduit).</summary>
    public Guid ConduitId { get; set; }

    /// <summary>Twitch's shard id (per conduit). Unique with <see cref="ConduitId"/>.</summary>
    public string ShardId { get; set; } = null!;

    /// <summary><c>webhook</c> | <c>websocket</c>.</summary>
    public string Transport { get; set; } = "webhook";

    public string? CallbackUrl { get; set; }

    public string? SessionId { get; set; }

    /// <summary><c>enabled</c> | <c>webhook_callback_verification_pending</c> | <c>disabled</c>.</summary>
    public string Status { get; set; } = "webhook_callback_verification_pending";

    public DateTime? AssignedAt { get; set; }

    public virtual EventSubConduit Conduit { get; set; } = null!;
}
