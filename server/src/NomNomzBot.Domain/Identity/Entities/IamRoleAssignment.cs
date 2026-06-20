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
/// Assigns an <see cref="IamRole"/> to an <see cref="IamPrincipal"/> (roles-permissions schema C.5, Plane C),
/// optionally scoped to one channel (<c>ScopeChannelId</c> null = platform-wide). <c>RevokedAt</c>/
/// <c>ExpiresAt</c> end the assignment. Unique per <c>(PrincipalId, RoleId, ScopeChannelId)</c>. SaaS-only.
/// </summary>
public class IamRoleAssignment : BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PrincipalId { get; set; }
    public Guid RoleId { get; set; }
    public Guid? ScopeChannelId { get; set; }
    public Guid? AssignedByPrincipalId { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? Reason { get; set; }
}
