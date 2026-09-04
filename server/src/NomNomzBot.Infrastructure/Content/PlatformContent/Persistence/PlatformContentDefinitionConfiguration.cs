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
using NomNomzBot.Domain.PlatformContent.Entities;

namespace NomNomzBot.Infrastructure.Content.PlatformContent.Persistence;

public class PlatformContentDefinitionConfiguration : IEntityTypeConfiguration<PlatformContentDefinition>
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
