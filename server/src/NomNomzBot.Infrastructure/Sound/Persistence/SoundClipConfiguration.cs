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
        builder.Property(e => e.CooldownSeconds).HasDefaultValue(0);
        builder.Property(e => e.MinPermissionLevel).HasDefaultValue(0);
        builder.Property(e => e.TriggerWord).HasMaxLength(50);

        builder.HasIndex(e => e.BroadcasterId);
        builder.HasIndex(e => e.CreatedByUserId);
        // Unique slug per channel (spec D5: play_sound resolves by id or Name).
        builder.HasIndex(e => new { e.BroadcasterId, e.Name }).IsUnique();
        // One soundboard trigger word per channel. A NULL TriggerWord is DISTINCT under both Postgres and
        // SQLite unique indexes, so many clips can leave the trigger unset while a set word stays unique.
        builder.HasIndex(e => new { e.BroadcasterId, e.TriggerWord }).IsUnique();

        builder
            .HasIndex(e => e.PlatformSourceDefinitionId)
            .HasDatabaseName("IX_SoundClip_PlatformSourceDefinitionId");
    }
}
