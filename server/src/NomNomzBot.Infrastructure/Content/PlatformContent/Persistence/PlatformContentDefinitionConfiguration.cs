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
using NomNomzBot.Domain.PlatformContent.Entities;

namespace NomNomzBot.Infrastructure.Content.PlatformContent.Persistence;

public class PlatformContentDefinitionConfiguration
    : IEntityTypeConfiguration<PlatformContentDefinition>
{
    public void Configure(EntityTypeBuilder<PlatformContentDefinition> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Kind).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Key).IsRequired().HasMaxLength(100);
        builder.Property(e => e.DisplayName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(1000);

        // Natural key: unique per Kind (§3.1).
        builder.HasIndex(e => new { e.Kind, e.Key }).IsUnique();
    }
}
