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

public class PlatformConnectionConfiguration : IEntityTypeConfiguration<PlatformConnection>
{
    public void Configure(EntityTypeBuilder<PlatformConnection> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).IsRequired();

        builder.Property(e => e.ChannelId).IsRequired();

        builder.Property(e => e.Provider).IsRequired().HasMaxLength(20);

        builder.Property(e => e.ExternalChannelId).IsRequired().HasMaxLength(100);

        builder
            .HasIndex(e => new { e.Provider, e.ExternalChannelId })
            .IsUnique()
            .HasDatabaseName("IX_PlatformConnection_Provider_ExternalChannelId");

        builder.HasIndex(e => e.ChannelId).HasDatabaseName("IX_PlatformConnection_ChannelId");

        builder.Property(e => e.DisplayName).IsRequired().HasMaxLength(255);

        builder.Property(e => e.IsPrimary).IsRequired();

        builder.Property(e => e.IsLive).IsRequired();

        builder
            .HasOne(e => e.Channel)
            .WithMany(c => c.PlatformConnections)
            .HasForeignKey(e => e.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
