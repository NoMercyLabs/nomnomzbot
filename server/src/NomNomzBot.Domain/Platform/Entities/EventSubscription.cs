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
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Domain.Platform.Entities;

public class EventSubscription : BaseEntity, ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public Guid BroadcasterId { get; set; }

    [MaxLength(50)]
    public string Provider { get; set; } = null!;

    [MaxLength(100)]
    public string EventType { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    public bool Enabled { get; set; } = true;

    [MaxLength(50)]
    public string? Version { get; set; }

    [MaxLength(255)]
    public string? SubscriptionId { get; set; }

    [MaxLength(255)]
    public string? SessionId { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = new();
    public string[] Condition { get; set; } = [];

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}
