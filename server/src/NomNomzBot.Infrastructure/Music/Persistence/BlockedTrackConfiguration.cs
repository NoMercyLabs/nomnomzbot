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
