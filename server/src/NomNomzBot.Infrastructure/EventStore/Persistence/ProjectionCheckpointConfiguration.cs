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
using NomNomzBot.Domain.EventStore.Entities;

namespace NomNomzBot.Infrastructure.EventStore.Persistence;

/// <summary>
/// Maps the per-projection consume cursor (schema O.3). Unique <c>(ProjectionName, BroadcasterId)</c> — one
/// checkpoint per projection per scope (a global projection's scope is the single null-tenant row).
/// </summary>
public class ProjectionCheckpointConfiguration : IEntityTypeConfiguration<ProjectionCheckpoint>
{
    public void Configure(EntityTypeBuilder<ProjectionCheckpoint> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.ProjectionName).IsRequired().HasMaxLength(150);
        builder
            .HasIndex(e => e.ProjectionName)
            .HasDatabaseName("IX_ProjectionCheckpoint_ProjectionName");

        builder.Property(e => e.BroadcasterId);
        builder
            .HasIndex(e => e.BroadcasterId)
            .HasDatabaseName("IX_ProjectionCheckpoint_BroadcasterId");

        builder.Property(e => e.LastPosition).IsRequired();
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);
        builder.Property(e => e.LastError);
        builder.Property(e => e.LastProcessedAt);
        builder.Property(e => e.UpdatedAt).IsRequired();

        builder
            .HasIndex(e => new { e.ProjectionName, e.BroadcasterId })
            .IsUnique()
            .HasDatabaseName("UX_ProjectionCheckpoint_ProjectionName_BroadcasterId");
    }
}
