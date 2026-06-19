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
using NomNomzBot.Domain.Tts.Entities;

namespace NomNomzBot.Infrastructure.Tts.Persistence;

public class TtsCacheEntryConfiguration : IEntityTypeConfiguration<TtsCacheEntry>
{
    public void Configure(EntityTypeBuilder<TtsCacheEntry> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ContentHash).IsRequired().HasMaxLength(64);

        builder.Property(e => e.AudioData).IsRequired();

        builder.Property(e => e.DurationMs).IsRequired();

        builder.Property(e => e.Provider).IsRequired().HasMaxLength(50);

        builder.Property(e => e.VoiceId).IsRequired().HasMaxLength(255);

        builder
            .HasIndex(e => e.ContentHash)
            .IsUnique()
            .HasDatabaseName("IX_TtsCacheEntry_ContentHash");
    }
}
