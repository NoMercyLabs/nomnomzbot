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

public class DiscordGuildConnectionConfiguration : IEntityTypeConfiguration<DiscordGuildConnection>
{
    public void Configure(EntityTypeBuilder<DiscordGuildConnection> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.GuildId).IsRequired().HasMaxLength(50);
        builder.Property(e => e.GuildName).HasMaxLength(255);
        builder.Property(e => e.ServerConsentStatus).IsRequired().HasMaxLength(20);
        builder.Property(e => e.ApprovedByDiscordUserId).HasMaxLength(50);

        // The both-opt-in handshake is unique per (tenant, guild).
        builder.HasIndex(e => new { e.BroadcasterId, e.GuildId }).IsUnique();
        builder.HasIndex(e => e.GuildId);

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
