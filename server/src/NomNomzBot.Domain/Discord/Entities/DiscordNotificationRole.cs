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
/// A per-streamer self-assign "notify me" Discord role (schema P.10). Members opt in via a button message (or
/// command) to be pinged when a notification fires. Unique on (GuildConnectionId, DiscordRoleId).
/// </summary>
public class DiscordNotificationRole : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }

    public Guid GuildConnectionId { get; set; }

    /// <summary>The external Discord role snowflake id — an indexed attribute, never a key.</summary>
    [MaxLength(50)]
    public string DiscordRoleId { get; set; } = null!;

    [MaxLength(255)]
    public string? RoleName { get; set; }

    public bool SelfAssignEnabled { get; set; }

    /// <summary>This role also DELIVERS BY DM: on dispatch, its opted-in members each get the rendered
    /// notification as a direct message (spec: discord-notifications, decided 2026-07-17).</summary>
    public bool DmEnabled { get; set; }

    /// <summary>The id of the posted opt-in button message (set by <c>PostOptInButtonAsync</c>).</summary>
    [MaxLength(50)]
    public string? ButtonMessageId { get; set; }

    /// <summary>The Discord channel the opt-in button message was posted to.</summary>
    [MaxLength(50)]
    public string? ButtonChannelId { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(GuildConnectionId))]
    public virtual DiscordGuildConnection GuildConnection { get; set; } = null!;
}
