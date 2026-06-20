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
    }
}
