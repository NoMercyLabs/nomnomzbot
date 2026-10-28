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
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

/// <summary>Maps one shard of an app-global EventSub conduit (schema §F.9). Unique <c>(ConduitId, ShardId)</c>.</summary>
public class EventSubConduitShardConfiguration : IEntityTypeConfiguration<EventSubConduitShard>
{
    public void Configure(EntityTypeBuilder<EventSubConduitShard> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ConduitId).IsRequired();
        builder.Property(e => e.ShardId).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Transport).IsRequired().HasMaxLength(20);
        builder.Property(e => e.CallbackUrl).HasMaxLength(2048);
        builder.Property(e => e.SessionId).HasMaxLength(255);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(40);

        builder
            .HasIndex(e => new { e.ConduitId, e.ShardId })
            .IsUnique()
            .HasDatabaseName("UX_EventSubConduitShard_ConduitId_ShardId");
    }
}
