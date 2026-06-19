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

public class EventSubscriptionConfiguration : IEntityTypeConfiguration<EventSubscription>
{
    public void Configure(EntityTypeBuilder<EventSubscription> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).IsRequired().HasMaxLength(50);

        builder.Property(e => e.BroadcasterId).IsRequired().HasMaxLength(50);

        builder.Property(e => e.Provider).IsRequired().HasMaxLength(50);

        builder.Property(e => e.EventType).IsRequired().HasMaxLength(100);

        builder.Property(e => e.Description).HasMaxLength(500);

        builder.Property(e => e.Enabled).HasDefaultValue(true);

        builder.Property(e => e.Version).HasMaxLength(50);

        builder.Property(e => e.SubscriptionId).HasMaxLength(255);

        builder.Property(e => e.SessionId).HasMaxLength(255);

        builder.Property(e => e.Metadata).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");

        builder.Property(e => e.Condition).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
