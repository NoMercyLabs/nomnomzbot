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

public class ComplianceAuditLogConfiguration : IEntityTypeConfiguration<ComplianceAuditLog>
{
    public void Configure(EntityTypeBuilder<ComplianceAuditLog> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.RequestType).IsRequired().HasMaxLength(20);
        builder.Property(e => e.SubjectIdHash).IsRequired().HasMaxLength(64);
        builder.Property(e => e.RequestedBy).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Outcome).IsRequired().HasMaxLength(20);

        builder
            .Property(e => e.TablesAffected)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb");

        builder.HasIndex(e => e.SubjectIdHash);
        builder.HasIndex(e => e.ErasureRequestId);
    }
}
