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
