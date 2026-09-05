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
using NomNomzBot.Domain.Billing.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class EntitlementGrantConfiguration : IEntityTypeConfiguration<EntitlementGrant>
{
    public void Configure(EntityTypeBuilder<EntitlementGrant> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Reason).IsRequired().HasMaxLength(500);

        // Resolution scans only LIVE grants for a broadcaster — this index makes that scan an index seek.
        builder.HasIndex(e => new { e.BroadcasterId, e.ExpiresAt });
    }
}
