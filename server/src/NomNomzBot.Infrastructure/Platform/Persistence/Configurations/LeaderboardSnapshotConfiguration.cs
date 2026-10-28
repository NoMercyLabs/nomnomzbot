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
using NomNomzBot.Domain.Economy.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class LeaderboardSnapshotConfiguration : IEntityTypeConfiguration<LeaderboardSnapshot>
{
    public void Configure(EntityTypeBuilder<LeaderboardSnapshot> builder)
    {
        builder.HasKey(e => e.Id); // long identity

        builder.Property(e => e.PeriodKey).IsRequired().HasMaxLength(20);
        builder.Property(e => e.SubjectTwitchUserId).IsRequired().HasMaxLength(50);
        builder.Property(e => e.DisplayNameSnapshot).IsRequired().HasMaxLength(255);

        builder.HasIndex(e => new { e.LeaderboardConfigId, e.PeriodKey });
    }
}
