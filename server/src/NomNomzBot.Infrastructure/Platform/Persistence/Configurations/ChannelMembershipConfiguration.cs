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

public class ChannelMembershipConfiguration : IEntityTypeConfiguration<ChannelMembership>
{
    public void Configure(EntityTypeBuilder<ChannelMembership> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ManagementRole).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Source).HasConversion<string>().HasMaxLength(20);

        // One ACTIVE membership per (channel, user), enforced at the DB via a partial unique index (matching the
        // soft-delete filter) so concurrent grant/sync writes can't race a duplicate row in — the service-level
        // guard alone let ~58 duplicate (channel, user) rows accumulate. Also serves the per-user lookups the
        // resolver / Gate-1 run.
        builder
            .HasIndex(e => new { e.BroadcasterId, e.UserId })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");
    }
}
