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
using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Infrastructure.Moderation.Persistence;

/// <summary>One channel's share of a moderator's mass ban, with its target accounts.</summary>
public class MassBanBatchConfiguration : IEntityTypeConfiguration<MassBanBatch>
{
    public void Configure(EntityTypeBuilder<MassBanBatch> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.OperatorDisplayName).HasMaxLength(100);
        builder.Property(e => e.ChannelTwitchId).HasMaxLength(50);
        builder.Property(e => e.ChannelLogin).HasMaxLength(100);
        builder.Property(e => e.DecidedByDisplayName).HasMaxLength(100);

        // The worker reads unfinished batches; the chat command reads the open batches of one channel.
        builder.HasIndex(e => new { e.CompletedAt, e.RequestedAt });
        builder.HasIndex(e => new { e.ChannelId, e.CompletedAt });

        builder
            .HasMany(e => e.Targets)
            .WithOne()
            .HasForeignKey(t => t.MassBanBatchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>One account inside a mass-ban batch.</summary>
public class MassBanBatchTargetConfiguration : IEntityTypeConfiguration<MassBanBatchTarget>
{
    public void Configure(EntityTypeBuilder<MassBanBatchTarget> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TwitchUserId).HasMaxLength(50);
        builder.Property(e => e.Reason).HasMaxLength(500);
        builder.Property(e => e.Error).HasMaxLength(500);
        builder.HasIndex(e => new { e.MassBanBatchId, e.ProcessedAt });
    }
}
