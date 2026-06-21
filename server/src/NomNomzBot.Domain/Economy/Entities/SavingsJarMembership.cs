// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Economy.Entities;

/// <summary>
/// A channel's membership in a shared savings jar (economy.md K.5). The cross-tenant access seam: a mutation
/// is allowed only for a channel with a <c>Status=Accepted</c> membership. CROSS-TENANT (NOT
/// <c>ITenantScoped</c> — the owner must see all members' rows). Per-channel contribution/withdrawal caps bound
/// abuse. One per <c>(JarId, MemberBroadcasterId)</c>.
/// </summary>
public class SavingsJarMembership : SoftDeletableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid JarId { get; set; }
    public Guid MemberBroadcasterId { get; set; }
    public JarRole Role { get; set; }
    public JarMembershipStatus Status { get; set; }
    public long? ContributionCapPerStream { get; set; }
    public long? WithdrawalCap { get; set; }
    public Guid? InvitedByBroadcasterId { get; set; }
    public DateTime? AcceptedAt { get; set; }
}
