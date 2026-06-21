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

public class LeaderboardSnapshotConfiguration : IEntityTypeConfiguration<LeaderboardSnapshot>
{
    public void Configure(EntityTypeBuilder<LeaderboardSnapshot> builder)
    {
        builder.HasKey(e => e.Id); // long identity

        builder.Property(e => e.PeriodKey).IsRequired().HasMaxLength(20);
        builder.Property(e => e.SubjectTwitchUserId).IsRequired().HasMaxLength(50);
        builder.Property(e => e.DisplayNameSnapshot).IsRequired().HasMaxLength(255);

        builder.HasIndex(e => new { e.LeaderboardConfigId, e.PeriodKey });
    }
}
