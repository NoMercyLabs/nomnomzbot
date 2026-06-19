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

namespace NomNomzBot.Infrastructure.Stream.Persistence;

public class StreamConfiguration : IEntityTypeConfiguration<global::NomNomzBot.Domain.Stream.Entities.Stream>
{
    public void Configure(EntityTypeBuilder<global::NomNomzBot.Domain.Stream.Entities.Stream> builder)
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
