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
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Widgets.Persistence;

public class WidgetVersionConfiguration : IEntityTypeConfiguration<WidgetVersion>
{
    public void Configure(EntityTypeBuilder<WidgetVersion> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.WidgetId).IsRequired();

        builder.Property(e => e.BuildStatus).IsRequired().HasMaxLength(20);
        builder.Property(e => e.ContentHash).HasMaxLength(64);

        builder.HasIndex(e => e.BroadcasterId);
        builder.HasIndex(e => e.WidgetId);
        builder.HasIndex(e => e.ContentHash);

        // One row per (widget, version) — append-only, not soft-deletable, so a true unique.
        builder.HasIndex(e => new { e.WidgetId, e.VersionNumber }).IsUnique();
    }
}
