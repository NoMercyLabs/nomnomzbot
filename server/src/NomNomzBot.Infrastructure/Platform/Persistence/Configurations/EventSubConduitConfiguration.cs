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
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

/// <summary>
/// Maps the app-global EventSub conduit (schema §F.8). Platform-level (not tenant-scoped). Unique
/// <c>ConduitId</c>. The conduit transport that provisions these is deferred (SaaS profile).
/// </summary>
public class EventSubConduitConfiguration : IEntityTypeConfiguration<EventSubConduit>
{
    public void Configure(EntityTypeBuilder<EventSubConduit> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Provider).IsRequired().HasMaxLength(20);
        builder.Property(e => e.ConduitId).IsRequired().HasMaxLength(255);
        builder.Property(e => e.ShardCount).IsRequired();
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);

        builder
            .HasIndex(e => e.ConduitId)
            .IsUnique()
            .HasDatabaseName("UX_EventSubConduit_ConduitId");

        builder
            .HasMany(e => e.Shards)
            .WithOne(s => s.Conduit)
            .HasForeignKey(s => s.ConduitId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
