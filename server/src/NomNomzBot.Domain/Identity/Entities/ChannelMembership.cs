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

using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Identity.Entities;

/// <summary>
/// A user's channel-management (Plane B) role in one channel (roles-permissions schema B.1). Sourced from a
/// Twitch mod/editor badge, Helix editors, an explicit bot grant, or channel ownership. <c>LevelValue</c> is
/// the denormalized ladder value of <see cref="ManagementRole"/> for fast Gate-1/Gate-2 comparison.
/// Unique per <c>(BroadcasterId, UserId)</c>.
/// </summary>
public class ChannelMembership : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }
    public Guid UserId { get; set; }
    public ManagementRole ManagementRole { get; set; }
    public int LevelValue { get; set; }
    public MembershipSource Source { get; set; }
    public DateTime GrantedAt { get; set; }
    public Guid? GrantedByUserId { get; set; }
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>
    /// This moderator's own yes: on a channel that did not onboard, spam defence may delete, time out and ban
    /// in this moderator's name while they read its chat. Off until the moderator turns it on (Stoney,
    /// 2026-11-19: "Your own yes is enough"). A moderator who loses the role and gets it back starts at off.
    /// </summary>
    public bool RemovesSpamInOwnName { get; set; }
}
