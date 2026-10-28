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

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class ConfigurationEntityConfiguration
    : IEntityTypeConfiguration<Domain.Platform.Entities.Configuration>
{
    public void Configure(EntityTypeBuilder<Domain.Platform.Entities.Configuration> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId);

        builder.Property(e => e.Key).IsRequired().HasMaxLength(255);

        builder.Property(e => e.SecureValue).HasMaxLength(4096);

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
