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
using NomNomzBot.Domain.Alerts.Entities;

namespace NomNomzBot.Infrastructure.Alerts.Persistence;

public class AlertQueueEntryConfiguration : IEntityTypeConfiguration<AlertQueueEntry>
{
    public void Configure(EntityTypeBuilder<AlertQueueEntry> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Provider).IsRequired().HasMaxLength(50);

        builder.Property(e => e.Kind).IsRequired().HasMaxLength(100);

        builder.Property(e => e.PayloadJson).IsRequired().HasColumnType("jsonb");

        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);

        // Read pattern is "the queue for this broadcaster, in order" plus prune-on-write — same shape as
        // IX_RenderedAlertCapture_BroadcasterId_CreatedAt.
        builder
            .HasIndex(e => new { e.BroadcasterId, e.CreatedAt })
            .HasDatabaseName("IX_AlertQueueEntry_BroadcasterId_CreatedAt");
    }
}
