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
