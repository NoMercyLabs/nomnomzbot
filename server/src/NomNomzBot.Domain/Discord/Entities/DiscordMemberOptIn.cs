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

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(NotificationRoleId))]
    public virtual DiscordNotificationRole NotificationRole { get; set; } = null!;
}
