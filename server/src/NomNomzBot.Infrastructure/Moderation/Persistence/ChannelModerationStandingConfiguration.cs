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
using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Infrastructure.Moderation.Persistence;

public class ChannelModerationStandingConfiguration
    : IEntityTypeConfiguration<ChannelModerationStanding>
{
    public void Configure(EntityTypeBuilder<ChannelModerationStanding> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Provider).IsRequired().HasMaxLength(20);
        builder.Property(e => e.UserId).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Standing).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Reason).HasMaxLength(500);

        // One standing per (channel, platform identity) — clear = hard delete, absence = normal.
        builder
            .HasIndex(e => new
            {
                e.BroadcasterId,
                e.Provider,
                e.UserId,
            })
            .IsUnique();
        // The registry's cache load + panel listing by tier.
        builder.HasIndex(e => new { e.BroadcasterId, e.Standing });
    }
}
