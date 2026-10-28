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

        // One live override per (broadcaster, target): a person has a single custom line per channel, used
        // for the shoutout and for raiding them. A soft-deleted row frees its slot for a re-add.
        builder
            .HasIndex(e => new { e.BroadcasterId, e.TargetTwitchUserId })
            .IsUnique()
            .HasDatabaseName("IX_ShoutoutOverride_Broadcaster_Target")
            .HasFilter("\"DeletedAt\" IS NULL");
    }
}
