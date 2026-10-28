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
using NomNomzBot.Infrastructure.Commands.Persistence;

namespace NomNomzBot.Infrastructure.Rewards.Persistence;

public class RewardConfiguration : IEntityTypeConfiguration<Reward>
{
    public void Configure(EntityTypeBuilder<Reward> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();

        builder.Property(e => e.Title).IsRequired().HasMaxLength(255);

        builder.Property(e => e.Response).HasMaxLength(2000);

        builder.Property(e => e.ReplyDelivery).HasReplyDeliveryColumn();

        builder
            .Property(e => e.Permission)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("everyone");

        builder.Property(e => e.IsEnabled).HasDefaultValue(true);

        builder.Property(e => e.SpeaksViaTts).HasDefaultValue(new Reward().SpeaksViaTts);

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
            .HasIndex(e => e.PlatformSourceDefinitionId)
            .HasDatabaseName("IX_Reward_PlatformSourceDefinitionId");

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
