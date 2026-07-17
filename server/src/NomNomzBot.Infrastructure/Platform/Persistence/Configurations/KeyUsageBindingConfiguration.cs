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
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

/// <summary>
/// Maps the DEK usage inventory (<see cref="KeyUsageBinding"/>, schema Q.2). The unique triple makes the
/// per-protect assertion idempotent at the database; the <c>CryptoKeyId</c> prefix of that index also serves
/// the rotation planner's "bindings of this key" lookup. Like <see cref="CryptoKeyConfiguration"/>, FKs are
/// declared as indexed columns without navigations — the registry is read across tenants and never soft-deleted.
/// </summary>
public class KeyUsageBindingConfiguration : IEntityTypeConfiguration<KeyUsageBinding>
{
    public void Configure(EntityTypeBuilder<KeyUsageBinding> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ResourceTable).IsRequired().HasMaxLength(100);
        builder.Property(e => e.ResourceColumn).IsRequired().HasMaxLength(100);

        builder
            .HasIndex(e => new
            {
                e.CryptoKeyId,
                e.ResourceTable,
                e.ResourceColumn,
            })
            .IsUnique();
        builder.HasIndex(e => e.BroadcasterId);
    }
}
