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
using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Infrastructure.Moderation.Persistence;

/// <summary>A moderator's recorded permission to include one channel in their mass bans.</summary>
public class ModeratorMassBanOptInConfiguration : IEntityTypeConfiguration<ModeratorMassBanOptIn>
{
    public void Configure(EntityTypeBuilder<ModeratorMassBanOptIn> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.BroadcasterTwitchId).HasMaxLength(50);
        builder.Property(e => e.BroadcasterLogin).HasMaxLength(100);
        builder.Property(e => e.Note).HasMaxLength(500);

        // One opt-in per moderator per channel; the planner looks them up by moderator.
        builder.HasIndex(e => new { e.OperatorUserId, e.BroadcasterTwitchId }).IsUnique();
    }
}
