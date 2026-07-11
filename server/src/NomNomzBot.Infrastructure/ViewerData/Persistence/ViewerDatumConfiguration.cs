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
using NomNomzBot.Domain.ViewerData.Entities;

namespace NomNomzBot.Infrastructure.ViewerData.Persistence;

public class ViewerDatumConfiguration : IEntityTypeConfiguration<ViewerDatum>
{
    public void Configure(EntityTypeBuilder<ViewerDatum> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.ViewerUserId).IsRequired();
        builder.Property(e => e.Key).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Value).IsRequired().HasMaxLength(500);

        // Concurrency token: adjust_viewer_data's read-modify-write retries on a lost race instead of
        // silently overwriting a concurrent increment (final value must equal the sum of all deltas).
        builder.Property(e => e.Value).IsConcurrencyToken();

        // Partial so a soft-deleted row never blocks re-creating the same key (soft-delete world).
        builder
            .HasIndex(e => new
            {
                e.BroadcasterId,
                e.ViewerUserId,
                e.Key,
            })
            .IsUnique()
            .HasDatabaseName("IX_ViewerDatum_BroadcasterId_ViewerUserId_Key")
            .HasFilter("\"DeletedAt\" IS NULL");

        builder.HasIndex(e => new { e.BroadcasterId, e.ViewerUserId });
    }
}
