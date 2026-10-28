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

/// <summary>
/// Durable song-request queue mirror (S001b). Restore reads every row for a channel ordered by
/// <see cref="SongRequestQueueItem.Sequence"/>, so that composite index is the only one this table needs.
/// </summary>
public class SongRequestQueueItemConfiguration : IEntityTypeConfiguration<SongRequestQueueItem>
{
    public void Configure(EntityTypeBuilder<SongRequestQueueItem> builder)
    {
        builder.HasKey(e => e.Id);

        builder
            .HasIndex(e => new { e.BroadcasterId, e.Sequence })
            .IsUnique()
            .HasDatabaseName("IX_SongRequestQueueItem_BroadcasterId_Sequence");
    }
}
