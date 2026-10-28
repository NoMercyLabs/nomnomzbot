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
using NomNomzBot.Domain.Discord.Entities;

namespace NomNomzBot.Infrastructure.Discord.Persistence;

public class DiscordNotificationDispatchConfiguration
    : IEntityTypeConfiguration<DiscordNotificationDispatch>
{
    public void Configure(EntityTypeBuilder<DiscordNotificationDispatch> builder)
    {
        builder.HasKey(e => e.Id); // UUIDv7 app-assigned

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.NotificationConfigId).IsRequired();
        builder.Property(e => e.TriggerType).IsRequired().HasMaxLength(30);
        builder.Property(e => e.DedupeKey).IsRequired().HasMaxLength(255);
        builder.Property(e => e.PostedMessageId).HasMaxLength(50);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);

        builder.HasIndex(e => e.NotificationConfigId);
        builder.HasIndex(e => e.DedupeKey);
        builder.HasIndex(e => e.StreamId);
        builder.HasIndex(e => e.DispatchedAt);

        // The DB-level dedupe guarantee: one post per (config, dedupe key). A duplicate insert IS the dedupe.
        builder.HasIndex(e => new { e.NotificationConfigId, e.DedupeKey }).IsUnique();

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(e => e.NotificationConfig)
            .WithMany()
            .HasForeignKey(e => e.NotificationConfigId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
