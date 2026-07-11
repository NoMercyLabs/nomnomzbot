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
using NomNomzBot.Domain.Supporters.Entities;

namespace NomNomzBot.Infrastructure.Supporters.Persistence;

public class SupporterConnectionConfiguration : IEntityTypeConfiguration<SupporterConnection>
{
    public void Configure(EntityTypeBuilder<SupporterConnection> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.SourceKey).IsRequired().HasMaxLength(30);
        builder.Property(e => e.ConnectionMode).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);

        // One connection per (broadcaster, source). The partial filter keeps the unique constraint from colliding
        // with soft-deleted rows, so a source can be reconnected after deletion (matching the ChannelMemberships
        // precedent for soft-delete-aware uniqueness; both Postgres and Sqlite quote identifiers this way).
        builder
            .HasIndex(e => new { e.BroadcasterId, e.SourceKey })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
