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
using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.Commands.Persistence;

public class NamedCounterConfiguration : IEntityTypeConfiguration<NamedCounter>
{
    public void Configure(EntityTypeBuilder<NamedCounter> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.Key).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Value).IsRequired().HasDefaultValue(0L);

        builder
            .HasIndex(e => new { e.BroadcasterId, e.Key })
            .IsUnique()
            .HasDatabaseName("IX_NamedCounter_BroadcasterId_Key");
    }
}
