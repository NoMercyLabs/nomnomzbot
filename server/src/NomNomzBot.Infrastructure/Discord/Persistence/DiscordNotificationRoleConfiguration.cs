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

public class DiscordNotificationRoleConfiguration
    : IEntityTypeConfiguration<DiscordNotificationRole>
{
    public void Configure(EntityTypeBuilder<DiscordNotificationRole> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.GuildConnectionId).IsRequired();
        builder.Property(e => e.DiscordRoleId).IsRequired().HasMaxLength(50);
        builder.Property(e => e.RoleName).HasMaxLength(255);
        builder.Property(e => e.ButtonMessageId).HasMaxLength(50);
        builder.Property(e => e.ButtonChannelId).HasMaxLength(50);

        builder.HasIndex(e => e.GuildConnectionId);
        builder.HasIndex(e => e.DiscordRoleId);

        // One notify role per (guild connection, Discord role).
        builder.HasIndex(e => new { e.GuildConnectionId, e.DiscordRoleId }).IsUnique();

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
