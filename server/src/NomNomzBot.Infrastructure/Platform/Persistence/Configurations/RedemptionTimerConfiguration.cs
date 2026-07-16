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
