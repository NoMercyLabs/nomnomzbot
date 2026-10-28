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
        builder.HasIndex(e => e.TargetPrincipalId);
        builder.HasIndex(e => e.PublishJobId);
    }
}
