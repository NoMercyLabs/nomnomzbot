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

public class GameConfigConfiguration : IEntityTypeConfiguration<GameConfig>
{
    public void Configure(EntityTypeBuilder<GameConfig> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.GameType).IsRequired().HasMaxLength(30);
        builder.Property(e => e.Category).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Permission).IsRequired().HasMaxLength(20);
        builder.Property(e => e.HouseEdgePercent).HasPrecision(5, 2);
        builder.Property(e => e.WinChancePercent).HasPrecision(5, 2);
        builder.Property(e => e.PayoutMultiplier).HasPrecision(8, 2);

        // One config per (channel, game type) — enforced in IGameService (soft-delete).
        builder.HasIndex(e => new { e.BroadcasterId, e.GameType });
    }
}
