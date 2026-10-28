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

public class NamedCounterConfiguration : IEntityTypeConfiguration<NamedCounter>
{
    public void Configure(EntityTypeBuilder<NamedCounter> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.Key).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Value).IsRequired().HasDefaultValue(0L);

        // Concurrency token: adjust_counter's read-modify-write retries on a lost race instead of
        // silently overwriting a concurrent increment (final value must equal the sum of all deltas).
        builder.Property(e => e.Value).IsConcurrencyToken();

        builder
            .HasIndex(e => new { e.BroadcasterId, e.Key })
            .IsUnique()
            .HasDatabaseName("IX_NamedCounter_BroadcasterId_Key");
    }
}
