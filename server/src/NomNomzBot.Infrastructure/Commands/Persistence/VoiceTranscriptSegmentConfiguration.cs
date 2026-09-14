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
