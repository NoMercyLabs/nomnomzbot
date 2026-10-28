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

public class VoiceTriggerConfiguration : IEntityTypeConfiguration<VoiceTrigger>
{
    public void Configure(EntityTypeBuilder<VoiceTrigger> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Word).IsRequired().HasMaxLength(200);

        // The report endpoint's hot path: one channel's enabled triggers, matched against the transcript.
        builder.HasIndex(e => new { e.BroadcasterId, e.IsEnabled });
    }
}
