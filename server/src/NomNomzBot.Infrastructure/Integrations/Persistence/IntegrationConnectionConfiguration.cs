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
using NomNomzBot.Domain.Integrations.Entities;

namespace NomNomzBot.Infrastructure.Integrations.Persistence;

public class IntegrationConnectionConfiguration : IEntityTypeConfiguration<IntegrationConnection>
{
    public void Configure(EntityTypeBuilder<IntegrationConnection> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Provider).IsRequired().HasMaxLength(20);
        builder.Property(e => e.ProviderAccountId).HasMaxLength(255);
        builder.Property(e => e.ProviderAccountName).HasMaxLength(255);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);
        builder.Property(e => e.ClientId).HasMaxLength(512);

        // [VC:JSON] List<string> granted scope set — jsonb, mirrors Service.Scopes.
        builder.Property(e => e.Scopes).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasIndex(e => new { e.BroadcasterId, e.Provider })
            .IsUnique()
            .HasDatabaseName("IX_IntegrationConnection_Broadcaster_Provider_Live")
            .HasFilter("\"DeletedAt\" IS NULL");
    }
}
