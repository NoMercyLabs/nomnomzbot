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
