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
