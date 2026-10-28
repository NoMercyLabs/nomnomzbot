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
