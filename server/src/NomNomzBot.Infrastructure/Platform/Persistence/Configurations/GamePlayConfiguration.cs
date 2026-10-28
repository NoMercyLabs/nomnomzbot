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

public class GamePlayConfiguration : IEntityTypeConfiguration<GamePlay>
{
    public void Configure(EntityTypeBuilder<GamePlay> builder)
    {
        builder.HasKey(e => e.Id); // long identity

        builder.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => new { e.BroadcasterId, e.GameConfigId });
        builder.HasIndex(e => new { e.BroadcasterId, e.PlayerUserId });
        // Live-game session history + settlement-idempotence reads (live-games.md D10).
        builder.HasIndex(e => new { e.BroadcasterId, e.GameSessionId });
    }
}
