// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
}
