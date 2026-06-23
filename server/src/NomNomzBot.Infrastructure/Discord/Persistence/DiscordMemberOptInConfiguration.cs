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

public class DiscordMemberOptInConfiguration : IEntityTypeConfiguration<DiscordMemberOptIn>
{
    public void Configure(EntityTypeBuilder<DiscordMemberOptIn> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.NotificationRoleId).IsRequired();
        builder.Property(e => e.DiscordMemberId).IsRequired().HasMaxLength(50);
        builder.Property(e => e.OptInSource).IsRequired().HasMaxLength(20);

        builder.HasIndex(e => e.NotificationRoleId);
        builder.HasIndex(e => e.DiscordMemberId);

        // One opt-in row per (notify role, member).
        builder.HasIndex(e => new { e.NotificationRoleId, e.DiscordMemberId }).IsUnique();

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(e => e.NotificationRole)
            .WithMany()
            .HasForeignKey(e => e.NotificationRoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
