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

        builder.Property(e => e.PlatformSourceHash).HasMaxLength(64);

        builder
            .HasIndex(e => e.PlatformSourceDefinitionId)
            .HasDatabaseName("IX_Pipeline_PlatformSourceDefinitionId");
    }
}
