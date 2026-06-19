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
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Identity.Entities;

public class ChannelModerator : SoftDeletableEntity, ITenantScoped
{
    [MaxLength(50)]
    public string ChannelId { get; set; } = null!;

    [MaxLength(50)]
    public string UserId { get; set; } = null!;

    [MaxLength(20)]
    public string Role { get; set; } = "moderator";

    // Stamped by the granting service via the injected TimeProvider (single clock,
    // platform-conventions §3.11) — entities do not self-stamp time.
    public DateTime GrantedAt { get; set; }

    [MaxLength(50)]
    public string? GrantedBy { get; set; }

    string ITenantScoped.BroadcasterId
    {
        get => ChannelId;
        set => ChannelId = value;
    }

    [ForeignKey(nameof(ChannelId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public virtual User User { get; set; } = null!;
}
