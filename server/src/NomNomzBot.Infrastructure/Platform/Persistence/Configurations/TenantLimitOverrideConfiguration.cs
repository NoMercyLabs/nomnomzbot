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

public class TenantLimitOverrideConfiguration : IEntityTypeConfiguration<TenantLimitOverride>
{
    public void Configure(EntityTypeBuilder<TenantLimitOverride> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.LimitKey).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Reason).IsRequired().HasMaxLength(500);

        // One LIVE override per (tenant, key) — DeletedAt is never NULL for a soft-deleted row (the
        // SoftDeletableEntity default is non-null-sentinel-free here because the unique index is scoped to
        // the live-row predicate below, so no NULL-distinctness footgun applies).
        builder
            .HasIndex(e => new { e.BroadcasterId, e.LimitKey })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");
    }
}
