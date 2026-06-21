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

public class IamRoleAssignmentConfiguration : IEntityTypeConfiguration<IamRoleAssignment>
{
    public void Configure(EntityTypeBuilder<IamRoleAssignment> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Reason).HasMaxLength(500);

        // Active-uniqueness per (principal, role, scope) is enforced by the service (RevokedAt makes a DB
        // unique index awkward); this index serves the per-principal effective-permission lookups.
        builder.HasIndex(e => new { e.PrincipalId, e.RoleId });
    }
}
