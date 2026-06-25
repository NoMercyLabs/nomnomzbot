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

public class RedemptionConfiguration : IEntityTypeConfiguration<Redemption>
{
    public void Configure(EntityTypeBuilder<Redemption> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.RedemptionId).IsRequired().HasMaxLength(80);
        builder.Property(e => e.RewardId).IsRequired().HasMaxLength(80);
        builder.Property(e => e.RewardTitle).IsRequired().HasMaxLength(255);
        builder.Property(e => e.UserId).IsRequired().HasMaxLength(50);
        builder.Property(e => e.UserDisplayName).IsRequired().HasMaxLength(255);
        builder.Property(e => e.UserInput).HasMaxLength(500);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);

        // One row per (channel, redemption) — the projection upserts on this natural key.
        builder.HasIndex(e => new { e.BroadcasterId, e.RedemptionId }).IsUnique();
        // The queue view: a channel's redemptions filtered by status, newest first.
        builder.HasIndex(e => new
        {
            e.BroadcasterId,
            e.Status,
            e.RedeemedAt,
        });
    }
}
