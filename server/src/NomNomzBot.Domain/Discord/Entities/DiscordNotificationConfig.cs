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
using NomNomzBot.Domain.Discord.ValueObjects;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Discord.Entities;

/// <summary>
/// One notification rule per (guild, trigger) (schema P.10): an event type → a target Discord channel → a
/// rendered message/embed, optionally pinging a single notify role. <see cref="EmbedConfig"/> is the
/// <c>[VC:JSON]</c> blob (Newtonsoft converter), upcast-anchored by <see cref="ConfigSchemaVersion"/>. Unique
/// on (GuildConnectionId, TriggerType).
/// </summary>
public class DiscordNotificationConfig : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }

    public Guid GuildConnectionId { get; set; }

    /// <summary><c>go_live</c> | <c>new_clip</c> | <c>schedule</c> | <c>milestone</c> [VC:enum].</summary>
    [MaxLength(30)]
    public string TriggerType { get; set; } = null!;

    public bool Enabled { get; set; }

    /// <summary>The target Discord channel snowflake id — an indexed attribute, never a key.</summary>
    [MaxLength(50)]
    public string TargetChannelId { get; set; } = null!;

    /// <summary>Single nullable notify role to ping (schema C4: one ping role per rule).</summary>
    public Guid? PingRoleId { get; set; }

    public string? MessageTemplate { get; set; }

    /// <summary>The embed shape, serialized via the Newtonsoft <c>[VC:JSON]</c> converter (text column).</summary>
    public DiscordEmbedConfig? EmbedConfig { get; set; }

    [MaxLength(20)]
    public string? MilestoneType { get; set; }

    public int? MilestoneThreshold { get; set; }

    /// <summary>Per-row upcast anchor for <see cref="EmbedConfig"/> (default 1); consumed on read by the config service.</summary>
    public int ConfigSchemaVersion { get; set; } = 1;

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(GuildConnectionId))]
    public virtual DiscordGuildConnection GuildConnection { get; set; } = null!;

    [ForeignKey(nameof(PingRoleId))]
    public virtual DiscordNotificationRole? PingRole { get; set; }
}
