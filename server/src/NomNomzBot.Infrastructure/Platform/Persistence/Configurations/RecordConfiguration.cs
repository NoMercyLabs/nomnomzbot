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

public class RecordConfiguration : IEntityTypeConfiguration<Record>
{
    public void Configure(EntityTypeBuilder<Record> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();

        builder.Property(e => e.RecordType).IsRequired().HasMaxLength(50);

        builder.Property(e => e.Data).IsRequired().HasColumnType("jsonb");

        builder.Property(e => e.UserId).IsRequired().HasMaxLength(50);

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);

        // UserId is the Twitch user id (indexed attribute), not an FK to Users.Id.
        builder.HasIndex(e => e.UserId).HasDatabaseName("IX_Record_UserId");

        builder
            .HasIndex(e => new { e.BroadcasterId, e.RecordType })
            .HasDatabaseName("IX_Record_BroadcasterId_RecordType");
    }
}
