// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Notifications.Entities;

/// <summary>
/// A persisted dismissal of one action-required inbox item (S-OWN22 T2). <see cref="ItemKey"/> is the item's
/// stable id from the aggregation (<c>held:{queueItemGuid}</c> or <c>token:{connectionId}:{ticks}</c> — a
/// grouped <c>held-user:</c> id is expanded into one row per contained <c>held:</c> key at dismiss time, so a
/// NEW hold from the same user after a dismissal surfaces again). Dead-token keys embed the invalidation
/// instant, so a token that dies again after a fix produces a fresh key an old dismissal cannot hide.
/// Unique per (ChannelId, ItemKey) among live rows. Tenant-scoped via <see cref="ChannelId"/> the same way
/// <c>ChannelModerator</c> is (explicit <see cref="ITenantScoped.BroadcasterId"/> over a
/// differently-named public key).
/// </summary>
public class ActionRequiredDismissal : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The owning channel (tenant key).</summary>
    public Guid ChannelId { get; set; }

    /// <summary>The dismissed item's stable key (never a grouped <c>held-user:</c> id — those are expanded).</summary>
    [MaxLength(200)]
    public string ItemKey { get; set; } = null!;

    /// <summary>The dashboard user who dismissed the item.</summary>
    public Guid DismissedByUserId { get; set; }

    // Stamped by the dismissing service via the injected TimeProvider (single clock,
    // platform-conventions §3.11) — entities do not self-stamp time.
    public DateTime DismissedAt { get; set; }

    Guid ITenantScoped.BroadcasterId
    {
        get => ChannelId;
        set => ChannelId = value;
    }
}
