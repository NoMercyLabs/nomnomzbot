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
using NomNomzBot.Domain.Billing.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class UsageRecordConfiguration : IEntityTypeConfiguration<UsageRecord>
{
    public void Configure(EntityTypeBuilder<UsageRecord> builder)
    {
        builder.HasKey(e => e.Id); // long identity

        builder.Property(e => e.MetricKey).IsRequired().HasMaxLength(50);

        // One counter per channel + metric + period (append-only).
        builder
            .HasIndex(e => new
            {
                e.BroadcasterId,
                e.MetricKey,
                e.PeriodStart,
            })
            .IsUnique();
        builder.HasIndex(e => e.PeriodStart);
    }
}
