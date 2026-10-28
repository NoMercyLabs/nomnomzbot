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

public class TierLimitConfiguration : IEntityTypeConfiguration<TierLimit>
{
    public void Configure(EntityTypeBuilder<TierLimit> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.LimitKey).IsRequired().HasMaxLength(50);

        builder.HasIndex(e => new { e.TierId, e.LimitKey }).IsUnique(); // GLOBAL config
    }
}
