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

public class DiscordLiveRoleConfigConfiguration : IEntityTypeConfiguration<DiscordLiveRoleConfig>
{
    public void Configure(EntityTypeBuilder<DiscordLiveRoleConfig> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.GuildConnectionId).IsRequired();
        builder.Property(e => e.RoleId).IsRequired().HasMaxLength(50);
        builder.Property(e => e.DiscordMemberId).IsRequired().HasMaxLength(50);
        builder.Property(e => e.AppliedDedupeKey).HasMaxLength(64);

        builder.HasIndex(e => e.GuildConnectionId);
        builder.HasIndex(e => e.RoleId);

        // One live-role rule per (streamer, guild link).
        builder.HasIndex(e => new { e.BroadcasterId, e.GuildConnectionId }).IsUnique();

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(e => e.GuildConnection)
            .WithMany()
            .HasForeignKey(e => e.GuildConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
