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
using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Infrastructure.Moderation.Persistence;

/// <summary>Schema J.6 — custom per-channel chat filters, many per channel, filtered by tenant + enabled flag.</summary>
public class ChatFilterConfiguration : IEntityTypeConfiguration<ChatFilter>
{
    public void Configure(EntityTypeBuilder<ChatFilter> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.BroadcasterId, e.IsEnabled });

        builder.Property(e => e.FilterType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Action).HasConversion<string>().HasMaxLength(20);
    }
}
