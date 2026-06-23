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
