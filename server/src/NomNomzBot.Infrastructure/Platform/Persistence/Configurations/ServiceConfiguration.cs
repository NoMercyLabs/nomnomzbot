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
