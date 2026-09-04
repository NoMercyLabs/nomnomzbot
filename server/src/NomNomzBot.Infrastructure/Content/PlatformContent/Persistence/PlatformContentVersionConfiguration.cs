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

public class PlatformContentVersionConfiguration : IEntityTypeConfiguration<PlatformContentVersion>
{
    public void Configure(EntityTypeBuilder<PlatformContentVersion> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ContentHash).IsRequired().HasMaxLength(64);
        builder.Property(e => e.PayloadJson).IsRequired();
        builder.Property(e => e.PublishNote).HasMaxLength(2000);

        // [VC:JSON] hand-rolled Newtonsoft converter, TEXT-as-JSON on both Postgres and SQLite.
        builder
            .Property(e => e.RenderGalleryRefs)
            .HasConversion(
                JsonValueConverter.Converter<List<string>>(),
                JsonValueConverter.Comparer<List<string>>()
            );

        builder.HasIndex(e => new { e.DefinitionId, e.Version }).IsUnique();

        builder
            .HasOne(e => e.Definition)
            .WithMany()
            .HasForeignKey(e => e.DefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
