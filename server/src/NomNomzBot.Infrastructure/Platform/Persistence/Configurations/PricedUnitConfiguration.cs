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

public class PricedUnitConfiguration : IEntityTypeConfiguration<PricedUnit>
{
    public void Configure(EntityTypeBuilder<PricedUnit> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.UnitKey).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Currency).IsRequired().HasMaxLength(3);

        // GLOBAL owner-authored config, never soft-deleted in practice — a live unique index (not filtered
        // to DeletedAt == null) so authoring the same UnitKey twice always fails fast rather than shadowing.
        builder.HasIndex(e => e.UnitKey).IsUnique();
    }
}
