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

public class TtsLexiconEntryConfiguration : IEntityTypeConfiguration<TtsLexiconEntry>
{
    public void Configure(EntityTypeBuilder<TtsLexiconEntry> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();

        builder.Property(e => e.Phrase).IsRequired().HasMaxLength(100);

        builder.Property(e => e.Replacement).IsRequired().HasMaxLength(200);

        builder.Property(e => e.MatchKind).IsRequired().HasMaxLength(10);

        // One live rule per (channel, phrase, matcher); a soft-deleted row frees the slot for a re-add.
        builder
            .HasIndex(e => new
            {
                e.BroadcasterId,
                e.Phrase,
                e.MatchKind,
            })
            .IsUnique()
            .HasDatabaseName("IX_TtsLexiconEntry_Broadcaster_Phrase_MatchKind")
            .HasFilter("\"DeletedAt\" IS NULL");
    }
}
