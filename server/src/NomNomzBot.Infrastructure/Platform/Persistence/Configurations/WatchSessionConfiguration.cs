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
using NomNomzBot.Domain.Analytics.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class WatchSessionConfiguration : IEntityTypeConfiguration<WatchSession>
{
    public void Configure(EntityTypeBuilder<WatchSession> builder)
    {
        builder.HasKey(e => e.Id); // bigint identity (append-only)

        builder.HasIndex(e => e.StreamId);
        builder.HasIndex(e => e.CreatedAt);
        builder.HasIndex(e => new { e.BroadcasterId, e.ViewerUserId }); // erasure scrub + watch history

        // One open/derived session per (channel, viewer, stream) — DB-enforced so a concurrent
        // GetOrOpenAsync race mints at most one row instead of double-counting watch time.
        builder
            .HasIndex(e => new
            {
                e.BroadcasterId,
                e.ViewerUserId,
                e.StreamId,
            })
            .IsUnique();
    }
}
