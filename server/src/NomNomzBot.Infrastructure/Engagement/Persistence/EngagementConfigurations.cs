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
using NomNomzBot.Domain.Engagement.Entities;

namespace NomNomzBot.Infrastructure.Engagement.Persistence;

public class EngagementConfigConfiguration : IEntityTypeConfiguration<EngagementConfig>
{
    public void Configure(EntityTypeBuilder<EngagementConfig> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.BroadcasterId).IsRequired();
        builder
            .Property(e => e.GreetCooldownSeconds)
            .HasDefaultValue(EngagementDefaults.GreetCooldownSeconds);
        builder.Property(e => e.ConfigSchemaVersion).HasDefaultValue(1);

        // One config per channel — partial so a soft-deleted row never blocks re-creating it.
        builder
            .HasIndex(e => e.BroadcasterId)
            .IsUnique()
            .HasDatabaseName("IX_EngagementConfig_BroadcasterId")
            .HasFilter("\"DeletedAt\" IS NULL");
    }
}

public class ViewerEngagementStateConfiguration : IEntityTypeConfiguration<ViewerEngagementState>
{
    public void Configure(EntityTypeBuilder<ViewerEngagementState> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.ViewerUserId).IsRequired();
        builder.Property(e => e.ViewerTwitchUserId).IsRequired().HasMaxLength(64);
        builder.Property(e => e.LastSeenStreamSessionId).HasMaxLength(50);
        builder.Property(e => e.LastGreetedStreamSessionId).HasMaxLength(50);

        // One state row per channel+viewer — partial for the same soft-delete reason.
        builder
            .HasIndex(e => new { e.BroadcasterId, e.ViewerUserId })
            .IsUnique()
            .HasDatabaseName("IX_ViewerEngagementState_BroadcasterId_ViewerUserId")
            .HasFilter("\"DeletedAt\" IS NULL");
    }
}
