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

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class PlatformEventResponseDefaultConfiguration
    : IEntityTypeConfiguration<PlatformEventResponseDefault>
{
    public void Configure(EntityTypeBuilder<PlatformEventResponseDefault> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EventType).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Message).HasMaxLength(2000);

        // Global catalogue keyed by the natural event type.
        builder.HasIndex(e => e.EventType).IsUnique();
    }
}
