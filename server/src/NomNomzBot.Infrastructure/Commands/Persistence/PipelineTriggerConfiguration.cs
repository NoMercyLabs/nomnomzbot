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

public class PipelineTriggerConfiguration : IEntityTypeConfiguration<PipelineTrigger>
{
    public void Configure(EntityTypeBuilder<PipelineTrigger> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.PipelineId).IsRequired();
        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.Kind).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Order).IsRequired();
        builder.Property(e => e.ConfigJson).IsRequired().HasDefaultValue("{}");
        builder.Property(e => e.IsEnabled).IsRequired().HasDefaultValue(true);

        builder
            .HasOne(e => e.Pipeline)
            .WithMany(p => p.Triggers)
            .HasForeignKey(e => e.PipelineId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasIndex(e => new { e.PipelineId, e.Order })
            .IsUnique()
            .HasDatabaseName("IX_PipelineTrigger_PipelineId_Order");

        builder.HasIndex(e => e.BroadcasterId).HasDatabaseName("IX_PipelineTrigger_BroadcasterId");
    }
}
