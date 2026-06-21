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
using NomNomzBot.Domain.Economy.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class LeaderboardConfigConfiguration : IEntityTypeConfiguration<LeaderboardConfig>
{
    public void Configure(EntityTypeBuilder<LeaderboardConfig> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Metric).IsRequired().HasMaxLength(30);
        builder.Property(e => e.Scope).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Period).IsRequired().HasMaxLength(20);

        builder.HasIndex(e => e.BroadcasterId);
        builder.HasIndex(e => e.JarId);
    }
}
