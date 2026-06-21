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

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class ActionDefinitionConfiguration : IEntityTypeConfiguration<ActionDefinition>
{
    public void Configure(EntityTypeBuilder<ActionDefinition> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ActionKey).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Plane).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.FloorTier).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Description).HasMaxLength(500);

        // Global catalogue keyed by the natural action key.
        builder.HasIndex(e => e.ActionKey).IsUnique();
    }
}
