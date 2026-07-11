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
using NomNomzBot.Domain.MediaShare.Entities;

namespace NomNomzBot.Infrastructure.MediaShare.Persistence;

public class MediaShareConfigConfiguration : IEntityTypeConfiguration<MediaShareConfig>
{
    public void Configure(EntityTypeBuilder<MediaShareConfig> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.RequireApproval).HasDefaultValue(true);
        builder.Property(e => e.AllowTwitchClips).HasDefaultValue(true);
        builder.Property(e => e.AllowYouTube).HasDefaultValue(true);
        builder.Property(e => e.MaxDurationSeconds).HasDefaultValue(180);
        builder.Property(e => e.MaxQueueLength).HasDefaultValue(20);
        builder.Property(e => e.PerUserCooldownSeconds).HasDefaultValue(60);
        builder.Property(e => e.ConfigSchemaVersion).HasDefaultValue(1);

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
