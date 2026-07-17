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
        builder.Property(e => e.ViewerVoiceSelfServiceEnabled).HasDefaultValue(true);

        builder
            .HasOne(e => e.SubjectKey)
            .WithMany()
            .HasForeignKey(e => e.SubjectKeyId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
