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
using NomNomzBot.Domain.Music.Entities;

namespace NomNomzBot.Infrastructure.Music.Persistence;

/// <summary>Held music writes. A flush reads one channel's rows oldest first, so that index is the only one needed.</summary>
public class MusicHeldWriteConfiguration : IEntityTypeConfiguration<MusicHeldWrite>
{
    public void Configure(EntityTypeBuilder<MusicHeldWrite> builder)
    {
        builder.HasKey(e => e.Id);

        builder
            .HasIndex(e => new { e.BroadcasterId, e.CreatedAt })
            .HasDatabaseName("IX_MusicHeldWrite_BroadcasterId_CreatedAt");
    }
}
