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
/// The join row binding an <see cref="IamPermission"/> to an <see cref="IamRole"/> (roles-permissions schema
/// C.3, Plane C). Unique per <c>(RoleId, PermissionId)</c>. SaaS-only.
/// </summary>
public class IamRolePermission : BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
}
