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
using NomNomzBot.Domain.MediaShare.Entities;

namespace NomNomzBot.Infrastructure.MediaShare.Persistence;

public class MediaShareConfigConfiguration : IEntityTypeConfiguration<MediaShareConfig>
{
    public void Configure(EntityTypeBuilder<MediaShareConfig> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.BroadcasterId).IsRequired();
        // The entity initializers are the one place a default is declared; the column defaults read them.
        MediaShareConfig defaults = new();
        builder.Property(e => e.RequireApproval).HasDefaultValue(defaults.RequireApproval);
        builder.Property(e => e.AllowTwitchClips).HasDefaultValue(defaults.AllowTwitchClips);
        builder.Property(e => e.AllowYouTube).HasDefaultValue(defaults.AllowYouTube);
        builder.Property(e => e.MaxDurationSeconds).HasDefaultValue(defaults.MaxDurationSeconds);
        builder.Property(e => e.MaxQueueLength).HasDefaultValue(defaults.MaxQueueLength);
        builder
            .Property(e => e.PerUserCooldownSeconds)
            .HasDefaultValue(defaults.PerUserCooldownSeconds);
        builder.Property(e => e.ConfigSchemaVersion).HasDefaultValue(defaults.ConfigSchemaVersion);

        builder
            .HasIndex(e => e.BroadcasterId)
            .IsUnique()
            .HasDatabaseName("IX_MediaShareConfig_BroadcasterId")
            .HasFilter("\"DeletedAt\" IS NULL");
    }
}

public class MediaShareRequestConfiguration : IEntityTypeConfiguration<MediaShareRequest>
{
    public void Configure(EntityTypeBuilder<MediaShareRequest> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.RequesterUserId).IsRequired();
        builder.Property(e => e.RequesterTwitchUserId).IsRequired().HasMaxLength(64);
        builder.Property(e => e.SourceType).IsRequired().HasMaxLength(20);
        builder.Property(e => e.SourceUrl).IsRequired().HasMaxLength(2048);
        builder.Property(e => e.MediaRef).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Title).HasMaxLength(300);
        builder.Property(e => e.ThumbnailUrl).HasMaxLength(2048);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);

        builder
            .HasIndex(e => new
            {
                e.BroadcasterId,
                e.Status,
                e.QueuePosition,
            })
            .HasDatabaseName("IX_MediaShareRequest_BroadcasterId_Status_QueuePosition");

        builder.HasIndex(e => new { e.BroadcasterId, e.RequesterUserId });
    }
}
