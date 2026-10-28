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
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Widgets.Persistence;

public class RenderedAlertCaptureConfiguration : IEntityTypeConfiguration<RenderedAlertCapture>
{
    public void Configure(EntityTypeBuilder<RenderedAlertCapture> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.EventType).IsRequired().HasMaxLength(100);

        builder.Property(c => c.Payload).IsRequired().HasColumnType("jsonb");

        builder.Property(c => c.ChannelEventId).HasMaxLength(50);

        // Read pattern is "most recent N per broadcaster" (prune-on-write + a later replay lookup) — same
        // shape as IX_ChannelEvent_ChannelId_CreatedAt.
        builder
            .HasIndex(c => new { c.BroadcasterId, c.CreatedAt })
            .HasDatabaseName("IX_RenderedAlertCapture_BroadcasterId_CreatedAt");

        // Replay's actual lookup: "find the capture(s) for the activity-feed item the operator clicked" —
        // by ChannelEventId, not type+recency. Not unique: a single origin event can fan out to multiple
        // subscribed widgets, each capturing its own row for the same ChannelEventId.
        builder
            .HasIndex(c => new { c.BroadcasterId, c.ChannelEventId })
            .HasDatabaseName("IX_RenderedAlertCapture_BroadcasterId_ChannelEventId");
    }
}
