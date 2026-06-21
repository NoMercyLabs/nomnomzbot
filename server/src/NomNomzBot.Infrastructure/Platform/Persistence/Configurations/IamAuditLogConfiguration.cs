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

public class IamAuditLogConfiguration : IEntityTypeConfiguration<IamAuditLog>
{
    public void Configure(EntityTypeBuilder<IamAuditLog> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Permission).IsRequired().HasMaxLength(60);
        builder.Property(e => e.PrincipalType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.TargetResource).HasMaxLength(150);
        builder.Property(e => e.SourceIpCipher).HasMaxLength(255);

        builder.HasIndex(e => e.PrincipalId);
        builder.HasIndex(e => e.OccurredAt);
    }
}
