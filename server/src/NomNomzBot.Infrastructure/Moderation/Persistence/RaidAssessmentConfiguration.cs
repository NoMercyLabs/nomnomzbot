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

public class RaidAssessmentConfiguration : IEntityTypeConfiguration<RaidAssessment>
{
    public void Configure(EntityTypeBuilder<RaidAssessment> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.RaiderTwitchUserId).IsRequired().HasMaxLength(50);
        builder.Property(e => e.RaiderLogin).IsRequired().HasMaxLength(50);
        builder.Property(e => e.RaiderDisplayName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Verdict).IsRequired().HasMaxLength(20);
        builder.Property(e => e.OccurredAt).IsRequired();

        builder
            .HasIndex(e => new { e.BroadcasterId, e.OccurredAt })
            .HasDatabaseName("IX_RaidAssessment_Broadcaster_OccurredAt");
    }
}
