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

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class ChannelCommunityStandingConfiguration
    : IEntityTypeConfiguration<ChannelCommunityStanding>
{
    public void Configure(EntityTypeBuilder<ChannelCommunityStanding> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Standing).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.SubTier).HasMaxLength(20);

        // Not soft-deletable — exactly one standing row per (channel, user).
        builder.HasIndex(e => new { e.BroadcasterId, e.UserId }).IsUnique();
    }
}
