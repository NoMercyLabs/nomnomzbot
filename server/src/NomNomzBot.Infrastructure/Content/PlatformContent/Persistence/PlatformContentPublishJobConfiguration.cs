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
using NomNomzBot.Domain.PlatformContent.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence.Converters;

namespace NomNomzBot.Infrastructure.Content.PlatformContent.Persistence;

public class PlatformContentPublishJobConfiguration
    : IEntityTypeConfiguration<PlatformContentPublishJob>
{
    public void Configure(EntityTypeBuilder<PlatformContentPublishJob> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Mode).IsRequired().HasMaxLength(40);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);
        builder.Property(e => e.FailureReason).HasMaxLength(2000);

        // [VC:JSON] hand-rolled Newtonsoft converter, TEXT-as-JSON on both Postgres and SQLite.
        builder
            .Property(e => e.RebuildFailedWidgetIds)
            .HasConversion(
                JsonValueConverter.Converter<List<Guid>>(),
                JsonValueConverter.Comparer<List<Guid>>()
            );

        builder
            .Property(e => e.ValidationFailedPipelineIds)
            .HasConversion(
                JsonValueConverter.Converter<List<Guid>>(),
                JsonValueConverter.Comparer<List<Guid>>()
            );

        builder.HasIndex(e => e.DefinitionId);
    }
}
