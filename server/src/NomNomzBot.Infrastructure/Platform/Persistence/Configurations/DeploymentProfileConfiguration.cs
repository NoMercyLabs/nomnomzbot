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
