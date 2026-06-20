// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Identity.Entities;

/// <summary>
/// A per-channel override of an <see cref="ActionDefinition"/>'s required level (roles-permissions schema
/// B.4). The override may only RAISE a floor for everyone (or lower it no further than the action's
/// <c>FloorLevel</c>) — it may never drop a Critical-tier capability onto a whole role tier (§0.2); per-user
/// reach into dangerous capabilities is the owner-issued <see cref="PermitGrant"/> path only. Unique per
/// <c>(BroadcasterId, ActionDefinitionId)</c>.
/// </summary>
public class ChannelActionOverride : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }
    public Guid ActionDefinitionId { get; set; }
    public int OverrideLevel { get; set; }
    public Guid? SetByUserId { get; set; }
}
