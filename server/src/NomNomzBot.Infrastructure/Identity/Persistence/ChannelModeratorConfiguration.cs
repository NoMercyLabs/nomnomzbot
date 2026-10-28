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
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Identity.Persistence;

public class ChannelModeratorConfiguration : IEntityTypeConfiguration<ChannelModerator>
{
    public void Configure(EntityTypeBuilder<ChannelModerator> builder)
    {
        builder.HasKey(e => new { e.ChannelId, e.UserId });

        builder.Property(e => e.ChannelId).IsRequired();

        builder.Property(e => e.UserId).IsRequired();

        builder.Property(e => e.Role).IsRequired().HasMaxLength(20).HasDefaultValue("moderator");

        builder.Property(e => e.GrantedAt).IsRequired();

        builder.Property(e => e.GrantedByUserId);

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
    }
}
