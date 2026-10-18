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
using NomNomzBot.Domain.Stream.Entities;

namespace NomNomzBot.Infrastructure.Stream.Persistence;

public class StreamRecapConfiguration : IEntityTypeConfiguration<StreamRecap>
{
    public void Configure(EntityTypeBuilder<StreamRecap> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ChannelId).IsRequired();

        builder.Property(e => e.StreamId).IsRequired().HasMaxLength(50);

        builder.Property(e => e.FromAt).IsRequired();

        builder.Property(e => e.ToAt).IsRequired();

        builder.Property(e => e.DataJson).IsRequired();

        // One recap per stream of a channel: a second raid in the same stream updates the row.
        builder
            .HasIndex(e => new { e.ChannelId, e.StreamId })
            .IsUnique()
            .HasDatabaseName("IX_StreamRecap_Channel_Stream");

        // The newest-first list of one channel.
        builder
            .HasIndex(e => new { e.ChannelId, e.FromAt })
            .HasDatabaseName("IX_StreamRecap_Channel_FromAt");

        builder
            .HasOne<Domain.Identity.Entities.Channel>()
            .WithMany()
            .HasForeignKey(e => e.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne<Domain.Stream.Entities.Stream>()
            .WithMany()
            .HasForeignKey(e => e.StreamId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
