// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
