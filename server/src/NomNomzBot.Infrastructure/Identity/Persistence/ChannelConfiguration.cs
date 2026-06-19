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

namespace NomNomzBot.Infrastructure.Identity.Persistence;

public class ChannelConfiguration : IEntityTypeConfiguration<Channel>
{
    public void Configure(EntityTypeBuilder<Channel> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).IsRequired().HasMaxLength(50);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(25);

        builder.Property(e => e.Enabled).HasDefaultValue(true);

        builder.Property(e => e.ShoutoutTemplate).HasMaxLength(450);

        builder.Property(e => e.ShoutoutInterval).HasDefaultValue(10);

        builder.Property(e => e.UsernamePronunciation).HasMaxLength(100);

        builder.Property(e => e.IsOnboarded).IsRequired();

        builder.Property(e => e.OverlayToken).IsRequired().HasMaxLength(36);

        builder.Property(e => e.IsLive).IsRequired();

        builder.Property(e => e.Language).HasMaxLength(50);

        builder.Property(e => e.GameId).HasMaxLength(50);

        builder.Property(e => e.GameName).HasMaxLength(255);

        builder.Property(e => e.Title).HasMaxLength(255);

        builder.Property(e => e.StreamDelay).IsRequired();

        builder.Property(e => e.Tags).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");

        builder
            .Property(e => e.ContentLabels)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb");

        builder.Property(e => e.IsBrandedContent).IsRequired();

        builder.HasIndex(e => e.OverlayToken).IsUnique().HasDatabaseName("IX_Channel_OverlayToken");

        builder.HasQueryFilter(e => e.DeletedAt == null);
    }
}
