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
using NomNomzBot.Domain.Quotes.Entities;

namespace NomNomzBot.Infrastructure.Quotes.Persistence;

public class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();

        builder.Property(e => e.Number).IsRequired();

        builder.Property(e => e.Text).IsRequired().HasMaxLength(500);

        builder.Property(e => e.QuotedDisplayName).HasMaxLength(100);

        builder.Property(e => e.ContextGame).HasMaxLength(100);

        builder.Property(e => e.QuotedAt);

        builder.Property(e => e.CreatedByUserId);

        // Unique + lookup index on the per-channel number (schema G.5). The soft-deleted row keeps its number
        // so the constraint guarantees a number is never reused, even after deletion (D1).
        builder.HasIndex(e => new { e.BroadcasterId, e.Number }).IsUnique();

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
