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

public class ViewerProfileConfiguration : IEntityTypeConfiguration<ViewerProfile>
{
    public void Configure(EntityTypeBuilder<ViewerProfile> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ViewerTwitchUserId).IsRequired().HasMaxLength(50);
        builder.Property(e => e.UsernameSnapshot).HasMaxLength(255);
        builder.Property(e => e.DisplayNameSnapshot).HasMaxLength(255);
        builder.Property(e => e.SubTier).HasMaxLength(10);

        builder.HasIndex(e => e.ViewerTwitchUserId);
        builder.HasIndex(e => e.LastSeenAt);
        // One profile per (channel, viewer) — projection-enforced via upsert; soft-deletable, so a plain index.
        builder.HasIndex(e => new { e.BroadcasterId, e.ViewerUserId });
    }
}
