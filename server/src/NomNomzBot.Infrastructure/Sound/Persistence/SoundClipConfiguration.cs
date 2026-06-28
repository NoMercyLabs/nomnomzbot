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
using NomNomzBot.Domain.Sound.Entities;

namespace NomNomzBot.Infrastructure.Sound.Persistence;

public class SoundClipConfiguration : IEntityTypeConfiguration<SoundClip>
{
    public void Configure(EntityTypeBuilder<SoundClip> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(50);
        builder.Property(e => e.DisplayName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.StorageKey).IsRequired().HasMaxLength(200);
        builder.Property(e => e.MimeType).IsRequired().HasMaxLength(40);
        builder.Property(e => e.DefaultVolume).HasDefaultValue(80);
        builder.Property(e => e.IsEnabled).HasDefaultValue(true);

        builder.HasIndex(e => e.BroadcasterId);
        builder.HasIndex(e => e.CreatedByUserId);
        // Unique slug per channel (spec D5: play_sound resolves by id or Name).
        builder.HasIndex(e => new { e.BroadcasterId, e.Name }).IsUnique();
    }
}
