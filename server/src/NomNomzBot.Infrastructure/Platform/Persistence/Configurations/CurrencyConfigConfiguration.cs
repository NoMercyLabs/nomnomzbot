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

public class CurrencyConfigConfiguration : IEntityTypeConfiguration<CurrencyConfig>
{
    public void Configure(EntityTypeBuilder<CurrencyConfig> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.CurrencyName).IsRequired().HasMaxLength(50);
        builder.Property(e => e.CurrencyNamePlural).HasMaxLength(50);

        // One config per channel — enforced in ICurrencyConfigService (soft-delete makes a DB unique awkward).
        builder.HasIndex(e => e.BroadcasterId);
    }
}
