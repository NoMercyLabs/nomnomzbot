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

public class SupporterEventConfiguration : IEntityTypeConfiguration<SupporterEvent>
{
    public void Configure(EntityTypeBuilder<SupporterEvent> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.SourceKey).IsRequired().HasMaxLength(30);
        builder.Property(e => e.Kind).IsRequired().HasMaxLength(20);
        builder.Property(e => e.SupporterDisplayName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Currency).HasMaxLength(3);
        builder.Property(e => e.Tier).HasMaxLength(50);
        builder.Property(e => e.ProviderTransactionId).IsRequired().HasMaxLength(120);

        // The dedup key: a redelivered webhook (same provider transaction) inserts once. Partial-filtered so a
        // soft-deleted event never blocks a genuinely new one with a reused id.
        builder
            .HasIndex(e => new
            {
                e.BroadcasterId,
                e.SourceKey,
                e.ProviderTransactionId,
            })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");

        // The events list reads newest-first per channel, optionally filtered by kind.
        builder.HasIndex(e => new { e.BroadcasterId, e.ReceivedAt });
        builder.HasIndex(e => new { e.BroadcasterId, e.Kind });

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Restrict);

        // The resolved supporter (when matchable) is optional; keep it Restrict + no back-collection.
        builder
            .HasOne(e => e.SupporterUser)
            .WithMany()
            .HasForeignKey(e => e.SupporterUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
