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
using NomNomzBot.Domain.Billing.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class PricedUnitConfiguration : IEntityTypeConfiguration<PricedUnit>
{
    public void Configure(EntityTypeBuilder<PricedUnit> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.UnitKey).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Currency).IsRequired().HasMaxLength(3);

        // GLOBAL owner-authored config, never soft-deleted in practice — a live unique index (not filtered
        // to DeletedAt == null) so authoring the same UnitKey twice always fails fast rather than shadowing.
        builder.HasIndex(e => e.UnitKey).IsUnique();
    }
}
