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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Discord.Entities;

/// <summary>
/// A single member's opt-in to a notify role (schema P.10) — who gets pinged. <see cref="OptedOutAt"/> set
/// (non-null) means they opted back out; the row is retained for history. Unique on
/// (NotificationRoleId, DiscordMemberId).
/// </summary>
public class DiscordMemberOptIn : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }

    public Guid NotificationRoleId { get; set; }

    /// <summary>The opted-in member's Discord user snowflake id — an indexed attribute [PII-hash], never a key.</summary>
    [MaxLength(50)]
    public string DiscordMemberId { get; set; } = null!;

    /// <summary><c>manual_role</c> | <c>command</c> | <c>button</c> [VC:enum].</summary>
    [MaxLength(20)]
    public string OptInSource { get; set; } = null!;

    public DateTime OptedInAt { get; set; }

    public DateTime? OptedOutAt { get; set; }

    /// <summary>Cached Discord DM channel snowflake — set on the first DM so repeat go-lives skip the open call.</summary>
    [MaxLength(32)]
    public string? DmChannelId { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(NotificationRoleId))]
    public virtual DiscordNotificationRole NotificationRole { get; set; } = null!;
}
