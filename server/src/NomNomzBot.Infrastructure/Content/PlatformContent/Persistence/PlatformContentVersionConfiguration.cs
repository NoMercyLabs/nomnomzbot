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
