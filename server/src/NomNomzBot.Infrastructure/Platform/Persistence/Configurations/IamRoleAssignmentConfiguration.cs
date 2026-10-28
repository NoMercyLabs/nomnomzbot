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
