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
using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Infrastructure.Moderation.Persistence;

public class ChannelModerationStandingConfiguration
    : IEntityTypeConfiguration<ChannelModerationStanding>
{
    public void Configure(EntityTypeBuilder<ChannelModerationStanding> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Provider).IsRequired().HasMaxLength(20);
        builder.Property(e => e.UserId).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Standing).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Reason).HasMaxLength(500);

        // One standing per (channel, platform identity) — clear = hard delete, absence = normal.
        builder
            .HasIndex(e => new
            {
                e.BroadcasterId,
                e.Provider,
                e.UserId,
            })
            .IsUnique();
        // The registry's cache load + panel listing by tier.
        builder.HasIndex(e => new { e.BroadcasterId, e.Standing });
    }
}
