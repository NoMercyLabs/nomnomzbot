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

public class TtsVoiceConfiguration : IEntityTypeConfiguration<TtsVoice>
{
    public void Configure(EntityTypeBuilder<TtsVoice> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).IsRequired().HasMaxLength(50);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(100);

        builder.Property(e => e.DisplayName).IsRequired().HasMaxLength(255);

        builder.Property(e => e.Locale).IsRequired().HasMaxLength(10);

        builder.Property(e => e.Gender).IsRequired().HasMaxLength(10);

        builder.Property(e => e.Provider).IsRequired().HasMaxLength(50);

        builder.Property(e => e.IsDefault).HasDefaultValue(false);

        builder.Property(e => e.Accent).HasMaxLength(50);
        builder.Property(e => e.Age).HasMaxLength(20);
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.PreviewUrl).HasMaxLength(2048);
        // StylesJson / TagsJson are unbounded JSON-array text columns (no length cap).

        // Accent is a first-class catalogue filter (browse "British female narration") — index it.
        builder.HasIndex(e => e.Accent);
    }
}
