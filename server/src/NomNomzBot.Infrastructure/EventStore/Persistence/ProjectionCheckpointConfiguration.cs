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
