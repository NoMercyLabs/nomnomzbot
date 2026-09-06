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

        builder.Property(e => e.Kind).IsRequired().HasMaxLength(20);

        // One live override per (broadcaster, target, KIND) — a person's shoutout line and their raid line
        // are two separate rows (ModerationController.SetShoutoutOverride looks them up the same way), so
        // the unique index must include Kind or the second kind's insert collides with the first's row.
        // A soft-deleted row frees its slot for a re-add.
        builder
            .HasIndex(e => new
            {
                e.BroadcasterId,
                e.TargetTwitchUserId,
                e.Kind,
            })
            .IsUnique()
            .HasDatabaseName("IX_ShoutoutOverride_Broadcaster_Target_Kind")
            .HasFilter("\"DeletedAt\" IS NULL");
    }
}
