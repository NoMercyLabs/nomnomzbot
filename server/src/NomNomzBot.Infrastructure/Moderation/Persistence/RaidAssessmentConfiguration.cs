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
using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Infrastructure.Moderation.Persistence;

public class RaidAssessmentConfiguration : IEntityTypeConfiguration<RaidAssessment>
{
    public void Configure(EntityTypeBuilder<RaidAssessment> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.RaiderTwitchUserId).IsRequired().HasMaxLength(50);
        builder.Property(e => e.RaiderLogin).IsRequired().HasMaxLength(50);
        builder.Property(e => e.RaiderDisplayName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Verdict).IsRequired().HasMaxLength(20);
        builder.Property(e => e.OccurredAt).IsRequired();

        builder
            .HasIndex(e => new { e.BroadcasterId, e.OccurredAt })
            .HasDatabaseName("IX_RaidAssessment_Broadcaster_OccurredAt");
    }
}
