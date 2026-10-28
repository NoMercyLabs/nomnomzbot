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
using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.Commands.Persistence;

public class VoiceTranscriptSegmentConfiguration : IEntityTypeConfiguration<VoiceTranscriptSegment>
{
    public void Configure(EntityTypeBuilder<VoiceTranscriptSegment> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Text).IsRequired().HasMaxLength(1000);
        builder.Property(e => e.StreamId).IsRequired().HasMaxLength(50);

        // The Analytics per-stream transcript read: one stream's segments, chronological.
        builder.HasIndex(e => new { e.StreamId, e.SpokenAt });
    }
}
