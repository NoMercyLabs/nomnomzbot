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

public class ChannelCommunityStandingConfiguration
    : IEntityTypeConfiguration<ChannelCommunityStanding>
{
    public void Configure(EntityTypeBuilder<ChannelCommunityStanding> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Standing).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.SubTier).HasMaxLength(20);

        // Not soft-deletable — exactly one standing row per (channel, user).
        builder.HasIndex(e => new { e.BroadcasterId, e.UserId }).IsUnique();
    }
}
