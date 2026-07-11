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
using NomNomzBot.Domain.Analytics.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class ChannelChatterDayConfiguration : IEntityTypeConfiguration<ChannelChatterDay>
{
    public void Configure(EntityTypeBuilder<ChannelChatterDay> builder)
    {
        builder.HasKey(e => e.Id); // bigint identity

        // One row per (channel, day, viewer-hash) — hashed identity, not soft-deletable, a true unique.
        builder
            .HasIndex(e => new
            {
                e.BroadcasterId,
                e.ActivityDate,
                e.ChatterHash,
            })
            .IsUnique();
    }
}
