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
using NomNomzBot.Domain.Stream.Entities;

namespace NomNomzBot.Infrastructure.Stream.Persistence;

public class ShoutoutOverrideConfiguration : IEntityTypeConfiguration<ShoutoutOverride>
{
    public void Configure(EntityTypeBuilder<ShoutoutOverride> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();

        builder.Property(e => e.TargetTwitchUserId).IsRequired().HasMaxLength(50);

        builder.Property(e => e.TargetDisplayName).IsRequired().HasMaxLength(50);

        builder.Property(e => e.MessageTemplate).IsRequired().HasMaxLength(1000);

        // One live override per (broadcaster, target); a soft-deleted row frees the slot for a re-add.
        builder
            .HasIndex(e => new { e.BroadcasterId, e.TargetTwitchUserId })
            .IsUnique()
            .HasDatabaseName("IX_ShoutoutOverride_Broadcaster_Target")
            .HasFilter("\"DeletedAt\" IS NULL");
    }
}
