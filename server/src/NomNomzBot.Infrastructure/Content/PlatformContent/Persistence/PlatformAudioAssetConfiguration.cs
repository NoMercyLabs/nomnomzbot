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
