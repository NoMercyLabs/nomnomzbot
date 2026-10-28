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

namespace NomNomzBot.Infrastructure.Content.PlatformContent.Persistence;

public class PlatformAudioAssetConfiguration : IEntityTypeConfiguration<PlatformAudioAsset>
{
    public void Configure(EntityTypeBuilder<PlatformAudioAsset> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.DisplayName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.FileName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.StorageKey).IsRequired().HasMaxLength(200);
        builder.Property(e => e.ContentType).IsRequired().HasMaxLength(40);
        builder.Property(e => e.ContentHash).IsRequired().HasMaxLength(64);

        // Upload refuses a second live copy of the same bytes; not unique, so a deleted copy never blocks it.
        builder.HasIndex(e => e.ContentHash);
    }
}
