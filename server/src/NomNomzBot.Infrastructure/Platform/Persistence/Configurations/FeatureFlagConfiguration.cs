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
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public void Configure(EntityTypeBuilder<FeatureFlag> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Key).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.MinTierKey).HasMaxLength(20);
        builder.Property(e => e.RequiresConsent).HasMaxLength(50);
        builder.Property(e => e.DeploymentMode).HasMaxLength(20);

        builder.HasIndex(e => e.Key).IsUnique();
        builder.HasIndex(e => e.MinTierId);
    }
}
