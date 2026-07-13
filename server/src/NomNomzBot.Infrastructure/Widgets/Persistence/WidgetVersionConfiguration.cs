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
