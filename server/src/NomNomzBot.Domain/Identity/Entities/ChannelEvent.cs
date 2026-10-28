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

public class ChannelEvent : BaseEntity
{
    [MaxLength(50)]
    public string Id { get; set; } = null!;

    // FK→Channels.Id (tenant) — string→Guid per schema §1.1.
    public Guid? ChannelId { get; set; }

    // FK→Users.Id — string→Guid per schema §1.1.
    public Guid? UserId { get; set; }

    // 100, not 50: Twitch EventSub / channel types run long — e.g.
    // "channel.channel_points_custom_reward_redemption.update" is 54 chars. At 50 the projection write
    // threw Npgsql 22001 (value too long) and the channel-event-log stalled on those events.
    [MaxLength(100)]
    public string Type { get; set; } = null!;

    public string? Data { get; set; }

    [ForeignKey(nameof(ChannelId))]
    public virtual Channel? Channel { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual User? User { get; set; }
}
