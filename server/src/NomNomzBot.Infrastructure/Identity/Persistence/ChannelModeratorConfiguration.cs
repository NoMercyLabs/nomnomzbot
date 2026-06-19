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

public class ChannelModeratorConfiguration : IEntityTypeConfiguration<ChannelModerator>
{
    public void Configure(EntityTypeBuilder<ChannelModerator> builder)
    {
        builder.HasKey(e => new { e.ChannelId, e.UserId });

        builder.Property(e => e.ChannelId).IsRequired().HasMaxLength(50);

        builder.Property(e => e.UserId).IsRequired().HasMaxLength(50);

        builder.Property(e => e.Role).IsRequired().HasMaxLength(20).HasDefaultValue("moderator");

        builder.Property(e => e.GrantedAt).IsRequired();

        builder.Property(e => e.GrantedBy).HasMaxLength(50);

        builder
            .HasOne(e => e.Channel)
            .WithMany(c => c.Moderators)
            .HasForeignKey(e => e.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(e => e.DeletedAt == null);
    }
}
