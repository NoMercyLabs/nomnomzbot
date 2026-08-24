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
/// The "currently live" Discord role rule (discord.md, live-role extension): while
/// <see cref="BroadcasterId"/>'s channel is live, <see cref="DiscordMemberId"/> holds
/// <see cref="RoleId"/> in the guild reached through <see cref="GuildConnectionId"/>; the role is removed the
/// moment the channel goes offline. Tenant-scoped to the streamer whose live state drives the role — a
/// friend's channel gets its OWN row (its own <see cref="DiscordGuildConnection"/> both-opt-in link into the
/// same guild, approved by that guild's admin and enabled by the friend) so a channel can never drive roles in
/// a guild it has no accepted link to. <see cref="IsCurrentlyApplied"/> + <see cref="AppliedDedupeKey"/> make
/// apply/remove idempotent and let the startup reconciler self-heal a role stranded by a missed offline event
/// (a stale <see cref="IsCurrentlyApplied"/> = true against an offline <see cref="Channel.IsLive"/> is cleared).
/// Unique on (BroadcasterId, GuildConnectionId).
/// </summary>
public class DiscordLiveRoleConfig : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }

    /// <summary>
    /// The both-opt-in link (schema P.10) into the guild this rule targets — the explicit consent that lets a
    /// friend's channel drive a role in someone else's guild; only an <c>Active</c> link (both consent flags
    /// true) is honored.
    /// </summary>
    public Guid GuildConnectionId { get; set; }

    /// <summary>The "currently live" Discord role snowflake id — an indexed attribute, never a key.</summary>
    [MaxLength(50)]
    public string RoleId { get; set; } = null!;

    /// <summary>
    /// The streamer's own Discord member snowflake id inside the guild — who the role is applied to and
    /// removed from. Entered by the streamer (there is no automatic Twitch-account-to-Discord-account link).
    /// </summary>
    [MaxLength(50)]
    public string DiscordMemberId { get; set; } = null!;

    public bool Enabled { get; set; }

    /// <summary>True while the role is believed applied — the idempotency + reconciliation flag.</summary>
    public bool IsCurrentlyApplied { get; set; }

    /// <summary>
    /// The dedupe discriminator of the online event that last applied the role (mirrors
    /// <c>DiscordGoLiveNotificationHandler</c>'s per-session key) — a duplicate online event for the same
    /// session is a no-op, not a second Discord call.
    /// </summary>
    [MaxLength(64)]
    public string? AppliedDedupeKey { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(GuildConnectionId))]
    public virtual DiscordGuildConnection GuildConnection { get; set; } = null!;
}
