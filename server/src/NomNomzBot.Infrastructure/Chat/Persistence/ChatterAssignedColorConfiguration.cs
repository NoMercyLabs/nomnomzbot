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
using NomNomzBot.Domain.Chat.Entities;

namespace NomNomzBot.Infrastructure.Chat.Persistence;

public class ChatterAssignedColorConfiguration : IEntityTypeConfiguration<ChatterAssignedColor>
{
    public void Configure(EntityTypeBuilder<ChatterAssignedColor> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.Provider).IsRequired().HasMaxLength(20);
        builder.Property(e => e.UserId).IsRequired().HasMaxLength(100);
        builder.Property(e => e.ColorHex).IsRequired().HasMaxLength(7);

        builder
            .HasIndex(e => new
            {
                e.BroadcasterId,
                e.Provider,
                e.UserId,
            })
            .IsUnique()
            .HasDatabaseName("IX_ChatterAssignedColor_Broadcaster_Provider_User");
    }
}
