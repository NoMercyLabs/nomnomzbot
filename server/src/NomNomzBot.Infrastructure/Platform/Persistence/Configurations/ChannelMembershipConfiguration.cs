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

        // One active membership per (channel, user) is enforced in MembershipService (soft-delete makes a DB
        // unique index awkward); this index serves the per-user lookups the resolver/Gate-1 run.
        builder.HasIndex(e => new { e.BroadcasterId, e.UserId });
    }
}
