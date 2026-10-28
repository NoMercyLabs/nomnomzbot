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
using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class PlatformEventResponseDefaultConfiguration
    : IEntityTypeConfiguration<PlatformEventResponseDefault>
{
    public void Configure(EntityTypeBuilder<PlatformEventResponseDefault> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EventType).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Message).HasMaxLength(2000);

        // Global catalogue keyed by the natural event type.
        builder.HasIndex(e => e.EventType).IsUnique();
    }
}
