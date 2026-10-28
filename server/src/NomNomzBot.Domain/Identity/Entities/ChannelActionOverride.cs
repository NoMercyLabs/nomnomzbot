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
