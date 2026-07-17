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
using NomNomzBot.Domain.Marketplace.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class InstalledBundleConfiguration : IEntityTypeConfiguration<InstalledBundle>
{
    public void Configure(EntityTypeBuilder<InstalledBundle> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(150);
        builder.Property(e => e.Source).IsRequired().HasMaxLength(20);
        builder.Property(e => e.MarketplaceItemId).HasMaxLength(64);
        builder.Property(e => e.Version).IsRequired().HasMaxLength(40);
        builder.Property(e => e.Author).HasMaxLength(100);
        builder.Property(e => e.License).HasMaxLength(40);
        builder.Property(e => e.ManifestJson).IsRequired();
        builder.Property(e => e.InstalledEntityIdsJson).IsRequired();

        // One live install per marketplace item per channel (re-install = update, never duplicate). The
        // MarketplaceItemId IS NOT NULL half keeps local ZIP installs (null item id) unlimited; the DeletedAt
        // half is the codebase's partial-unique-index convention so an uninstalled bundle can be re-installed
        // (both Postgres and Sqlite quote identifiers this way).
        builder
            .HasIndex(e => new
            {
                e.BroadcasterId,
                e.Source,
                e.MarketplaceItemId,
            })
            .IsUnique()
            .HasDatabaseName("IX_InstalledBundle_BroadcasterId_Source_MarketplaceItemId")
            .HasFilter("\"MarketplaceItemId\" IS NOT NULL AND \"DeletedAt\" IS NULL");

        // The installed-bundles listing.
        builder.HasIndex(e => e.BroadcasterId);
    }
}
