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
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

/// <summary>
/// Maps the single-row <see cref="DeploymentProfile"/> (P.12). All enum columns are stored as strings
/// (<c>HasConversion&lt;string&gt;</c>) so the row is human-readable and provider-portable across the
/// Postgres and SQLite migration sets. <see cref="DeploymentProfile.InstanceId"/> is unique.
/// </summary>
public class DeploymentProfileConfiguration : IEntityTypeConfiguration<DeploymentProfile>
{
    public void Configure(EntityTypeBuilder<DeploymentProfile> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Mode).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.DbProvider).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder
            .Property(e => e.CacheProvider)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder
            .Property(e => e.EventSubTransport)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(e => e.CodeExecutor).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.TokenVault).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder
            .Property(e => e.ExposureModel)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder
            .Property(e => e.DefaultGuidanceLevel)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(e => e.InstanceId).IsUnique();
    }
}
