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

namespace NomNomzBot.Infrastructure.Rewards.Persistence;

public class RewardConfiguration : IEntityTypeConfiguration<Reward>
{
    public void Configure(EntityTypeBuilder<Reward> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();

        builder.Property(e => e.Title).IsRequired().HasMaxLength(255);

        builder.Property(e => e.Response).HasMaxLength(2000);

        builder
            .Property(e => e.Permission)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("everyone");

        builder.Property(e => e.IsEnabled).HasDefaultValue(true);

        builder.Property(e => e.Description).HasMaxLength(500);

        builder.Property(e => e.PipelineJson).HasColumnType("jsonb");

        builder.Property(e => e.IsPlatform).IsRequired();

        builder.Property(e => e.IsManageable).IsRequired();

        builder.Property(e => e.TwitchRewardId).HasMaxLength(50);
        builder
            .HasIndex(e => new { e.BroadcasterId, e.TwitchRewardId })
            .IsUnique()
            .HasFilter("\"TwitchRewardId\" IS NOT NULL");

        builder.Property(e => e.Cost);

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
