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
