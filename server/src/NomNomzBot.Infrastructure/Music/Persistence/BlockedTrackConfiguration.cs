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
/// Blocked song-request tracks (music-sr.md, legacy <c>!bansong</c>): one live block per
/// (channel, provider, track URI) — the unique index is filtered to live rows so an unblocked
/// (soft-deleted) track can be blocked again.
/// </summary>
public class BlockedTrackConfiguration : IEntityTypeConfiguration<BlockedTrack>
{
    public void Configure(EntityTypeBuilder<BlockedTrack> builder)
    {
        builder.HasKey(e => e.Id);

        builder
            .HasIndex(e => new
            {
                e.BroadcasterId,
                e.Provider,
                e.TrackUri,
            })
            .IsUnique()
            .HasDatabaseName("IX_BlockedTrack_BroadcasterId_Provider_TrackUri")
            .HasFilter("\"DeletedAt\" IS NULL");

        // The admission-path lookup: per-tenant match on the URI alone (any provider).
        builder.HasIndex(e => new { e.BroadcasterId, e.TrackUri });
    }
}
