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
