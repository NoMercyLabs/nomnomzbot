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

public class TtsUsageRecordConfiguration : IEntityTypeConfiguration<TtsUsageRecord>
{
    public void Configure(EntityTypeBuilder<TtsUsageRecord> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();

        builder.Property(e => e.UserId).IsRequired().HasMaxLength(50);

        builder.Property(e => e.CharacterCount).IsRequired();

        builder.Property(e => e.Provider).IsRequired().HasMaxLength(50);

        builder.Property(e => e.VoiceId).IsRequired().HasMaxLength(255);

        builder.Property(e => e.Text).HasMaxLength(2000);

        builder.HasIndex(e => new { e.BroadcasterId, e.StreamId });
        builder.HasIndex(e => new { e.BroadcasterId, e.OccurredAt });
    }
}
