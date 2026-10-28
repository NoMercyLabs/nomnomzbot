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
using NomNomzBot.Domain.Tts.Entities;

namespace NomNomzBot.Infrastructure.Tts.Persistence;

/// <summary>Schema P.1 — one TTS config row per channel; BYOK ciphers ride as nullable envelope columns.</summary>
public class TtsConfigConfiguration : IEntityTypeConfiguration<TtsConfig>
{
    public void Configure(EntityTypeBuilder<TtsConfig> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.BroadcasterId).IsUnique();

        builder.Property(e => e.Mode).IsRequired().HasMaxLength(20);
        builder.Property(e => e.DefaultProvider).IsRequired().HasMaxLength(20);
        builder.Property(e => e.MinPermission).IsRequired().HasMaxLength(20);

        // Viewer self-service is on by default (spec decision 6) — a DB default of true backfills every
        // pre-existing channel row so the behavior matches a freshly-created config.
        // The value is read from the entity's own initializer so the default is declared in one place.
        builder
            .Property(e => e.ViewerVoiceSelfServiceEnabled)
            .HasDefaultValue(new TtsConfig().ViewerVoiceSelfServiceEnabled);

        // A DB default backfills every existing channel row with the no-cooldown behavior.
        builder.Property(e => e.CooldownSeconds).HasDefaultValue(new TtsConfig().CooldownSeconds);

        builder
            .HasOne(e => e.SubjectKey)
            .WithMany()
            .HasForeignKey(e => e.SubjectKeyId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
