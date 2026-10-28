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

namespace NomNomzBot.Infrastructure.Commands.Persistence;

public class PipelineExecutionConfiguration : IEntityTypeConfiguration<PipelineExecution>
{
    public void Configure(EntityTypeBuilder<PipelineExecution> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.TriggerKind).IsRequired().HasMaxLength(40);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);
        builder.Property(e => e.HostCallCount).IsRequired().HasDefaultValue(0);
        builder.Property(e => e.DurationMs).IsRequired().HasDefaultValue(0);
        builder.Property(e => e.ErrorMessage).HasMaxLength(2000);
        builder.Property(e => e.StartedAt).IsRequired();

        builder
            .HasIndex(e => new { e.PipelineId, e.StartedAt })
            .HasDatabaseName("IX_PipelineExecution_PipelineId_StartedAt");

        builder
            .HasIndex(e => new { e.BroadcasterId, e.StartedAt })
            .HasDatabaseName("IX_PipelineExecution_BroadcasterId_StartedAt");
    }
}
