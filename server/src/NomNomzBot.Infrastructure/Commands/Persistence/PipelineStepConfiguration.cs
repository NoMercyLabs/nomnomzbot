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

public class PipelineStepConfiguration : IEntityTypeConfiguration<PipelineStep>
{
    public void Configure(EntityTypeBuilder<PipelineStep> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.PipelineId).IsRequired();
        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.Order).IsRequired();
        builder.Property(e => e.ActionType).IsRequired().HasMaxLength(60);
        builder.Property(e => e.ConfigJson).IsRequired().HasDefaultValue("{}");
        builder.Property(e => e.ConfigSchemaVersion).IsRequired().HasDefaultValue(1);
        builder.Property(e => e.IsEnabled).IsRequired().HasDefaultValue(true);
        builder.Property(e => e.Branch).HasMaxLength(10);
        builder.Property(e => e.BlockKind).HasMaxLength(20);

        builder
            .HasOne(e => e.Pipeline)
            .WithMany(p => p.Steps)
            .HasForeignKey(e => e.PipelineId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasIndex(e => new { e.PipelineId, e.Order })
            .HasDatabaseName("IX_PipelineStep_PipelineId_Order");

        builder.HasIndex(e => e.BroadcasterId).HasDatabaseName("IX_PipelineStep_BroadcasterId");
    }
}
