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

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Identity.Entities;

public class ChannelModerator : SoftDeletableEntity, ITenantScoped
{
    // FK→Channels.Id (tenant) — string→Guid per schema §1.1.
    public Guid ChannelId { get; set; }

    // FK→Users.Id — string→Guid per schema §1.1.
    public Guid UserId { get; set; }

    [MaxLength(20)]
    public string Role { get; set; } = "moderator";

    // Stamped by the granting service via the injected TimeProvider (single clock,
    // platform-conventions §3.11) — entities do not self-stamp time.
    public DateTime GrantedAt { get; set; }

    public Guid? GrantedByUserId { get; set; }

    Guid ITenantScoped.BroadcasterId
    {
        get => ChannelId;
        set => ChannelId = value;
    }

    [ForeignKey(nameof(ChannelId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public virtual User User { get; set; } = null!;
}
