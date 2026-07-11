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
using NomNomzBot.Domain.Giveaways.Entities;

namespace NomNomzBot.Infrastructure.Giveaways.Persistence;

/// <summary>
/// Maps the giveaway campaign (giveaways.md G.6). No FK to Channels — the EventSubSubscriptions
/// pattern: soft-delete-only means a cascade could never fire; the tenant filter scopes reads.
/// </summary>
public class GiveawayConfiguration : IEntityTypeConfiguration<Giveaway>
{
    public void Configure(EntityTypeBuilder<Giveaway> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.Title).IsRequired().HasMaxLength(140);
        builder.Property(e => e.EntryMode).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Keyword).HasMaxLength(50);
        builder.Property(e => e.EligibilityJson).HasColumnType("text");
        builder.Property(e => e.WeightingJson).HasColumnType("text");
        builder.Property(e => e.PrizeMode).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);

        builder
            .HasIndex(e => new { e.BroadcasterId, e.Status })
            .HasDatabaseName("IX_Giveaway_Broadcaster_Status");
    }
}

/// <summary>Maps entries (G.7): unique per (GiveawayId, ViewerUserId) — the dedupe guarantee.</summary>
public class GiveawayEntryConfiguration : IEntityTypeConfiguration<GiveawayEntry>
{
    public void Configure(EntityTypeBuilder<GiveawayEntry> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.ViewerTwitchUserId).IsRequired().HasMaxLength(50);

        builder
            .HasIndex(e => new { e.GiveawayId, e.ViewerUserId })
            .IsUnique()
            .HasDatabaseName("UX_GiveawayEntry_Giveaway_Viewer");
        builder.HasIndex(e => e.BroadcasterId).HasDatabaseName("IX_GiveawayEntry_Broadcaster");
    }
}

/// <summary>Maps the append-only winner history (G.8).</summary>
public class GiveawayWinnerConfiguration : IEntityTypeConfiguration<GiveawayWinner>
{
    public void Configure(EntityTypeBuilder<GiveawayWinner> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.ViewerTwitchUserId).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);

        builder.HasIndex(e => e.GiveawayId).HasDatabaseName("IX_GiveawayWinner_Giveaway");
        builder
            .HasIndex(e => new { e.BroadcasterId, e.DrawnAt })
            .HasDatabaseName("IX_GiveawayWinner_Broadcaster_DrawnAt");
    }
}

/// <summary>Maps code pools (G.9).</summary>
public class GiveawayCodePoolConfiguration : IEntityTypeConfiguration<GiveawayCodePool>
{
    public void Configure(EntityTypeBuilder<GiveawayCodePool> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.Name).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Description).HasMaxLength(300);

        builder.HasIndex(e => e.BroadcasterId).HasDatabaseName("IX_GiveawayCodePool_Broadcaster");
    }
}

/// <summary>Maps the AEAD-encrypted codes (G.10); the claim query rides (CodePoolId, Status).</summary>
public class GiveawayCodeConfiguration : IEntityTypeConfiguration<GiveawayCode>
{
    public void Configure(EntityTypeBuilder<GiveawayCode> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.CodeCipher).IsRequired().HasColumnType("text");
        builder.Property(e => e.Label).HasMaxLength(100);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);

        builder
            .HasIndex(e => new { e.CodePoolId, e.Status })
            .HasDatabaseName("IX_GiveawayCode_Pool_Status");
    }
}
