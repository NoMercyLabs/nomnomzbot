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

public class ChannelLowTrustStatusConfiguration : IEntityTypeConfiguration<ChannelLowTrustStatus>
{
    public void Configure(EntityTypeBuilder<ChannelLowTrustStatus> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TwitchUserId).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);
        builder.Property(e => e.BanEvasionEvaluation).HasMaxLength(20);

        // One flag per (channel, chatter): cleared = hard delete, absence = none.
        builder.HasIndex(e => new { e.BroadcasterId, e.TwitchUserId }).IsUnique();
    }
}
