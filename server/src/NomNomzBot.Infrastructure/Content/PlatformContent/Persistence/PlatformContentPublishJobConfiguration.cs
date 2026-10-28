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

        builder
            .Property(e => e.ValidationFailedCodeScriptIds)
            .HasConversion(
                JsonValueConverter.Converter<List<Guid>>(),
                JsonValueConverter.Comparer<List<Guid>>()
            );

        builder
            .Property(e => e.UpdateFailedTemplateRowIds)
            .HasConversion(
                JsonValueConverter.Converter<List<Guid>>(),
                JsonValueConverter.Comparer<List<Guid>>()
            );

        builder.HasIndex(e => e.DefinitionId);
    }
}
