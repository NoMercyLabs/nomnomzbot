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
using NomNomzBot.Domain.Economy.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class LeaderboardOptOutConfiguration : IEntityTypeConfiguration<LeaderboardOptOut>
{
    public void Configure(EntityTypeBuilder<LeaderboardOptOut> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ViewerTwitchUserId).IsRequired().HasMaxLength(50);

        // One opt-out per (channel, viewer) — enforced in the service (soft-delete allows re-toggle).
        builder.HasIndex(e => new { e.BroadcasterId, e.ViewerUserId });
    }
}
