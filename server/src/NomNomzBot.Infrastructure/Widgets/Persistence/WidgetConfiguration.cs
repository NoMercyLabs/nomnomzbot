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
using NomNomzBot.Infrastructure.Platform.Persistence.Converters;

namespace NomNomzBot.Infrastructure.Widgets.Persistence;

public class WidgetConfiguration : IEntityTypeConfiguration<Widget>
{
    public void Configure(EntityTypeBuilder<Widget> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();

        builder.Property(e => e.Name).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Description).HasMaxLength(500);

        builder.Property(e => e.Framework).IsRequired().HasMaxLength(20).HasDefaultValue("vanilla");
        builder.Property(e => e.Source).IsRequired().HasMaxLength(20).HasDefaultValue("custom");

        builder.Property(e => e.IsEnabled).HasDefaultValue(true);
        builder.Property(e => e.ConfigSchemaVersion).HasDefaultValue(1);

        // [VC:JSON] — hand-rolled Newtonsoft converters (never jsonb / HasDefaultValueSql), so the same TEXT-as-JSON
        // mapping runs on Postgres and SQLite alike.
        builder
            .Property(e => e.EventSubscriptions)
            .HasConversion(
                JsonValueConverter.Converter<List<string>>(),
                JsonValueConverter.Comparer<List<string>>()
            );

        builder
            .Property(e => e.Settings)
            .HasConversion(
                JsonValueConverter.Converter<Dictionary<string, object>>(),
                JsonValueConverter.Comparer<Dictionary<string, object>>()
            );

        builder.HasIndex(e => e.BroadcasterId);
        builder.HasIndex(e => e.Source);
        builder.HasIndex(e => e.GalleryItemId);
        builder.HasIndex(e => e.ActiveVersionId);

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
