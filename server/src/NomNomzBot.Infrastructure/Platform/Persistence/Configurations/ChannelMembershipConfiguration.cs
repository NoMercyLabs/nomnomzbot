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
