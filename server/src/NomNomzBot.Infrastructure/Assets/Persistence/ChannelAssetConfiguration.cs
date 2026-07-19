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
using NomNomzBot.Domain.Assets.Entities;

namespace NomNomzBot.Infrastructure.Assets.Persistence;

public class ChannelAssetConfiguration : IEntityTypeConfiguration<ChannelAsset>
{
    public void Configure(EntityTypeBuilder<ChannelAsset> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(50);
        builder.Property(e => e.DisplayName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Kind).IsRequired().HasMaxLength(10);
        builder.Property(e => e.MimeType).IsRequired().HasMaxLength(40);
        builder.Property(e => e.StorageKey).IsRequired().HasMaxLength(200);

        builder.HasIndex(e => e.BroadcasterId);
        builder.HasIndex(e => e.CreatedByUserId);
        // One LIVE asset per (channel, name) — the stable serving URL's identity. Filtered so a
        // soft-deleted row does not block re-uploading the same name (unique-index audit convention).
        builder
            .HasIndex(e => new { e.BroadcasterId, e.Name })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");
    }
}
