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
using NomNomzBot.Domain.Rewards.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class RedemptionTimerConfiguration : IEntityTypeConfiguration<RedemptionTimer>
{
    public void Configure(EntityTypeBuilder<RedemptionTimer> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.RedemptionId).IsRequired().HasMaxLength(80);
        builder.Property(e => e.RewardId).IsRequired().HasMaxLength(80);
        builder.Property(e => e.RewardTitle).IsRequired().HasMaxLength(255);
        builder.Property(e => e.RedeemedByDisplayName).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);

        // One countdown per (channel, redemption) — a redelivered redemption event must not double-start.
        builder.HasIndex(e => new { e.BroadcasterId, e.RedemptionId }).IsUnique();
        // The dashboard view + the expiry ticker both read a channel's timers by status.
        builder.HasIndex(e => new { e.BroadcasterId, e.Status });
    }
}
