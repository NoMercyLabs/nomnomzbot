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

namespace NomNomzBot.Infrastructure.Stream.Persistence;

public class StreamConfiguration : IEntityTypeConfiguration<Domain.Stream.Entities.Stream>
{
    public void Configure(EntityTypeBuilder<Domain.Stream.Entities.Stream> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).IsRequired().HasMaxLength(50);

        builder.Property(e => e.ChannelId).IsRequired().HasMaxLength(50);

        builder.Property(e => e.Language).HasMaxLength(50);

        builder.Property(e => e.GameId).HasMaxLength(50);

        builder.Property(e => e.GameName).HasMaxLength(255);

        builder.Property(e => e.Title).HasMaxLength(255);

        builder.Property(e => e.Delay).IsRequired();

        builder.Property(e => e.Tags).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");

        builder
            .Property(e => e.ContentLabels)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb");

        builder.Property(e => e.IsBrandedContent).IsRequired();

        builder
            .HasOne(e => e.Channel)
            .WithMany(c => c.Streams)
            .HasForeignKey(e => e.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
