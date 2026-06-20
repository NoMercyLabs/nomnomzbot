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

public class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(50);

        builder.Property(e => e.Enabled).HasDefaultValue(true);

        builder.Property(e => e.ClientId).HasMaxLength(512);

        builder.Property(e => e.ClientSecret).HasMaxLength(512);

        builder.Property(e => e.UserName).HasMaxLength(255);

        builder.Property(e => e.UserId).HasMaxLength(50);

        builder.Property(e => e.Scopes).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");

        builder.Property(e => e.AccessToken).HasMaxLength(2048);

        builder.Property(e => e.RefreshToken).HasMaxLength(2048);

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasIndex(e => new { e.Name, e.BroadcasterId })
            .IsUnique()
            .HasDatabaseName("IX_Service_Name_BroadcasterId");
    }
}
