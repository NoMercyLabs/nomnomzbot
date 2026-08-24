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
