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

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class ScheduledPipelineTaskConfiguration : IEntityTypeConfiguration<ScheduledPipelineTask>
{
    public void Configure(EntityTypeBuilder<ScheduledPipelineTask> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.PipelineName).HasMaxLength(200);
        builder.Property(e => e.VariablesJson).IsRequired();
        builder.Property(e => e.TriggeredByUserId).IsRequired().HasMaxLength(100);
        builder.Property(e => e.TriggeredByDisplayName).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);
        builder.Property(e => e.DedupeKey).HasMaxLength(200);

        // The sweeper scans for due pending rows across tenants — index the exact predicate it filters on.
        builder.HasIndex(e => new { e.Status, e.DueAt });

        // At most one LIVE (pending) task per (channel, dedupe key): re-scheduling replaces rather than stacks.
        // Terminal rows (fired / cancelled / expired) keep their key but fall outside the partial index, so a key
        // is reusable once its prior run has resolved.
        builder
            .HasIndex(e => new { e.BroadcasterId, e.DedupeKey })
            .IsUnique()
            .HasFilter("\"Status\" = 'pending'");
    }
}
