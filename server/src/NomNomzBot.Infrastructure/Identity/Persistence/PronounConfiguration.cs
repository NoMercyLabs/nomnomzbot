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
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Identity.Persistence;

public class PronounConfiguration : IEntityTypeConfiguration<Pronoun>
{
    public void Configure(EntityTypeBuilder<Pronoun> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(50);

        builder.Property(e => e.Key).HasMaxLength(30);
        builder.HasIndex(e => e.Key).IsUnique().HasFilter("\"Key\" IS NOT NULL");

        builder.Property(e => e.Subject).IsRequired().HasMaxLength(20);

        builder.Property(e => e.Object).IsRequired().HasMaxLength(20);

        builder.Property(e => e.Possessive).IsRequired().HasMaxLength(20);

        builder.Property(e => e.GenderedTerm).IsRequired().HasMaxLength(20);

        builder.Property(e => e.Singular).IsRequired();
    }
}
