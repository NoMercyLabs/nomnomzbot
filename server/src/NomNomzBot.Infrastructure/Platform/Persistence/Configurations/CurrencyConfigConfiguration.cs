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

public class CurrencyConfigConfiguration : IEntityTypeConfiguration<CurrencyConfig>
{
    public void Configure(EntityTypeBuilder<CurrencyConfig> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.CurrencyName).IsRequired().HasMaxLength(50);
        builder.Property(e => e.CurrencyNamePlural).HasMaxLength(50);

        // One config per channel — enforced in ICurrencyConfigService (soft-delete makes a DB unique awkward).
        builder.HasIndex(e => e.BroadcasterId);
    }
}
