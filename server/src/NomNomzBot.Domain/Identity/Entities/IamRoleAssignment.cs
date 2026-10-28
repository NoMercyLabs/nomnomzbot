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
