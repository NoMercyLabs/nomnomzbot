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
using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.Commands.Persistence;

public class PipelineConfiguration : IEntityTypeConfiguration<Pipeline>
{
    public void Configure(EntityTypeBuilder<Pipeline> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder
            .Property(e => e.TriggerKind)
            .IsRequired()
            .HasMaxLength(40)
            .HasDefaultValue("manual");
        builder.Property(e => e.TriggerCount).IsRequired().HasDefaultValue(0L);
        builder.Property(e => e.MaxStepCount).IsRequired().HasDefaultValue(50);
        builder.Property(e => e.IsEnabled).IsRequired().HasDefaultValue(true);

        builder
            .HasIndex(e => new { e.BroadcasterId, e.IsEnabled })
            .HasDatabaseName("IX_Pipeline_BroadcasterId_IsEnabled");
    }
}
