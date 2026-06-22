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

public class TierLimitConfiguration : IEntityTypeConfiguration<TierLimit>
{
    public void Configure(EntityTypeBuilder<TierLimit> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.LimitKey).IsRequired().HasMaxLength(50);

        builder.HasIndex(e => new { e.TierId, e.LimitKey }).IsUnique(); // GLOBAL config
    }
}
