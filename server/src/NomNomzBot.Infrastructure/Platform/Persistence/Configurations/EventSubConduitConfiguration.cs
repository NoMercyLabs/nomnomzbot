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
