// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class IamRolePermissionConfiguration : IEntityTypeConfiguration<IamRolePermission>
{
    public void Configure(EntityTypeBuilder<IamRolePermission> builder)
    {
        builder.HasKey(e => e.Id);

        // Hard-deleted join rows — one row per (role, permission).
        builder.HasIndex(e => new { e.RoleId, e.PermissionId }).IsUnique();
    }
}
