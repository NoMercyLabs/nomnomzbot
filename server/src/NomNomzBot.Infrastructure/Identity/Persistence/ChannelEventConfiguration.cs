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
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Identity.Persistence;

public class ChannelEventConfiguration : IEntityTypeConfiguration<ChannelEvent>
{
    public void Configure(EntityTypeBuilder<ChannelEvent> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).IsRequired().HasMaxLength(50);

        builder.Property(e => e.ChannelId).HasMaxLength(50);

        builder.Property(e => e.UserId).HasMaxLength(50);

        // 100, not 50: long Twitch channel types (e.g. "channel.channel_points_custom_reward_redemption.update",
        // 54 chars) overflowed varchar(50) → Npgsql 22001, stalling the channel-event-log projection.
        builder.Property(e => e.Type).IsRequired().HasMaxLength(100);

        builder.Property(e => e.Data).HasColumnType("jsonb");

        builder
            .HasOne(e => e.Channel)
            .WithMany(c => c.Events)
            .HasForeignKey(e => e.ChannelId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasIndex(e => new { e.ChannelId, e.Type })
            .HasDatabaseName("IX_ChannelEvent_ChannelId_Type");

        builder
            .HasIndex(e => new { e.ChannelId, e.CreatedAt })
            .HasDatabaseName("IX_ChannelEvent_ChannelId_CreatedAt");
    }
}
