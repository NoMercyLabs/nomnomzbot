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
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Identity.Persistence;

public class ChannelConfiguration : IEntityTypeConfiguration<Channel>
{
    public void Configure(EntityTypeBuilder<Channel> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).IsRequired();

        builder.Property(e => e.OwnerUserId).IsRequired();

        builder.HasIndex(e => e.OwnerUserId).IsUnique().HasDatabaseName("IX_Channel_OwnerUserId");

        builder.Property(e => e.TwitchChannelId).HasMaxLength(50);

        builder
            .HasIndex(e => e.TwitchChannelId)
            .IsUnique()
            .HasDatabaseName("IX_Channel_TwitchChannelId");

        builder.Property(e => e.Provider).IsRequired().HasMaxLength(20);

        builder.Property(e => e.ExternalChannelId).IsRequired().HasMaxLength(100);

        builder
            .HasIndex(e => new { e.Provider, e.ExternalChannelId })
            .IsUnique()
            .HasDatabaseName("IX_Channel_Provider_ExternalChannelId");

        builder.Property(e => e.Name).IsRequired().HasMaxLength(25);

        builder.Property(e => e.NameNormalized).IsRequired().HasMaxLength(25);

        builder.HasIndex(e => e.NameNormalized).HasDatabaseName("IX_Channel_NameNormalized");

        builder.Property(e => e.SongRequestPageToken).HasMaxLength(64);
        builder
            .HasIndex(e => e.SongRequestPageToken)
            .IsUnique()
            .HasDatabaseName("IX_Channel_SongRequestPageToken");

        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);

        builder.Property(e => e.SuspendedReason).HasMaxLength(500);

        builder.Property(e => e.DeploymentMode).IsRequired().HasMaxLength(20);

        builder.Property(e => e.BillingTierKey).IsRequired().HasMaxLength(20);

        builder.Property(e => e.Enabled).HasDefaultValue(true);

        builder.Property(e => e.ShoutoutTemplate).HasMaxLength(450);

        builder.Property(e => e.ShoutoutInterval).HasDefaultValue(10);

        builder.Property(e => e.UsernamePronunciation).HasMaxLength(100);

        // Built-in-command voice ([VC:enum] PersonalityTone). Store default = Informative so existing rows
        // backfill to the polite default on migration and a new row never carries a null tone.
        builder
            .Property(e => e.Personality)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue(NomNomzBot.Domain.Identity.Enums.PersonalityTone.Informative);

        builder.Property(e => e.IsOnboarded).IsRequired();

        builder.Property(e => e.OverlayToken).IsRequired().HasMaxLength(36);

        builder.Property(e => e.IsLive).IsRequired();

        builder.Property(e => e.Language).HasMaxLength(50);

        builder.Property(e => e.GameId).HasMaxLength(50);

        builder.Property(e => e.GameName).HasMaxLength(255);

        builder.Property(e => e.Title).HasMaxLength(255);

        builder.Property(e => e.StreamDelay).IsRequired();

        builder.Property(e => e.Tags).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");

        builder
            .Property(e => e.ContentLabels)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb");

        builder.Property(e => e.IsBrandedContent).IsRequired();

        builder.HasIndex(e => e.OverlayToken).IsUnique().HasDatabaseName("IX_Channel_OverlayToken");
    }
}
