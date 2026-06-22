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
using NomNomzBot.Domain.Economy.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class CatalogPurchaseConfiguration : IEntityTypeConfiguration<CatalogPurchase>
{
    public void Configure(EntityTypeBuilder<CatalogPurchase> builder)
    {
        builder.HasKey(e => e.Id); // long identity

        builder.Property(e => e.ItemNameSnapshot).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.IdempotencyKey).HasMaxLength(100);

        builder.HasIndex(e => new { e.BroadcasterId, e.CatalogItemId });
        builder.HasIndex(e => new { e.BroadcasterId, e.BuyerUserId });
        builder.HasIndex(e => new { e.BroadcasterId, e.IdempotencyKey }); // idempotency lookup
    }
}
