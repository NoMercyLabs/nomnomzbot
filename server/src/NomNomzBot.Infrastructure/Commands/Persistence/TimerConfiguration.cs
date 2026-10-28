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
using DomainTimer = NomNomzBot.Domain.Commands.Entities.Timer;

namespace NomNomzBot.Infrastructure.Commands.Persistence;

public class TimerConfiguration : IEntityTypeConfiguration<DomainTimer>
{
    public void Configure(EntityTypeBuilder<DomainTimer> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.IntervalMinutes).IsRequired().HasDefaultValue(30);
        builder.Property(e => e.MinChatActivity).IsRequired().HasDefaultValue(0);
        builder.Property(e => e.IsEnabled).IsRequired().HasDefaultValue(true);
        builder.Property(e => e.SpeakWithTts).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.ReplyDelivery).HasReplyDeliveryColumn();
        builder.Property(e => e.ConfigSchemaVersion).IsRequired().HasDefaultValue(1);

        builder.Property(e => e.Messages).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(e => e.Pipeline)
            .WithMany()
            .HasForeignKey(e => e.PipelineId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder
            .HasIndex(e => new { e.BroadcasterId, e.IsEnabled })
            .HasDatabaseName("IX_Timer_BroadcasterId_IsEnabled");

        builder
            .HasIndex(e => e.PlatformSourceDefinitionId)
            .HasDatabaseName("IX_Timer_PlatformSourceDefinitionId");
    }
}
