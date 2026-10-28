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

        builder.Property(e => e.UserId);

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

        // Nullable, best-effort attribution (owner punch list 2026-09-08 §3) — a hard-deleted User (GDPR
        // erasure) should never take the quote down with it, so SetNull rather than Cascade/Restrict.
        builder
            .HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasIndex(e => new { e.BroadcasterId, e.UserId })
            .HasDatabaseName("IX_Quote_Broadcaster_UserId");
    }
}
