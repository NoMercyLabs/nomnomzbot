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
/// An individual <c>!permit</c> grant (roles-permissions schema B.5): either a whole role
/// (<c>GrantType=Role</c>, <see cref="GrantedRole"/> set) or a single capability
/// (<c>GrantType=Capability</c>, <see cref="ActionDefinitionId"/> set). The effective level is
/// <c>MAX(badge role, bot-role grants, capability grants)</c>, never escalating above the grantor's own
/// level; a Critical-tier capability is delegated only this way (to a named user), never via a role-tier
/// override. <c>RevokedAt</c>/<c>ExpiresAt</c> end the grant. Indexed by <c>(BroadcasterId, UserId)</c>.
/// </summary>
public class PermitGrant : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }
    public Guid UserId { get; set; }
    public PermitGrantType GrantType { get; set; }
    public ManagementRole? GrantedRole { get; set; }
    public Guid? ActionDefinitionId { get; set; }
    public Guid GrantedByUserId { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? Reason { get; set; }
}
