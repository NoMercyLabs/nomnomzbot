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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Identity.Persistence;

public class ChannelEventConfiguration : IEntityTypeConfiguration<ChannelEvent>
{
    public void Configure(EntityTypeBuilder<ChannelEvent> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).IsRequired().HasMaxLength(50);

        builder.Property(e => e.ChannelId).HasMaxLength(50);

        builder.Property(e => e.UserId).HasMaxLength(50);

        builder.Property(e => e.Type).IsRequired().HasMaxLength(50);

        builder.Property(e => e.Data).HasColumnType("jsonb");

        builder
            .HasOne(e => e.Channel)
            .WithMany(c => c.Events)
            .HasForeignKey(e => e.ChannelId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasIndex(e => new { e.ChannelId, e.Type })
            .HasDatabaseName("IX_ChannelEvent_ChannelId_Type");

        builder
            .HasIndex(e => new { e.ChannelId, e.CreatedAt })
            .HasDatabaseName("IX_ChannelEvent_ChannelId_CreatedAt");
    }
}
