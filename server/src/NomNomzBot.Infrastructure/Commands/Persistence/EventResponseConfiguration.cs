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
using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.Commands.Persistence;

public class EventResponseConfiguration : IEntityTypeConfiguration<EventResponse>
{
    public void Configure(EntityTypeBuilder<EventResponse> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.EventType).IsRequired().HasMaxLength(80);
        builder
            .Property(e => e.ResponseType)
            .IsRequired()
            .HasMaxLength(40)
            .HasDefaultValue("chat_message");
        builder.Property(e => e.Message).HasMaxLength(2000);
        builder.Property(e => e.ReplyDelivery).HasReplyDeliveryColumn();
        builder.Property(e => e.IsEnabled).IsRequired().HasDefaultValue(true);
        builder.Property(e => e.ConfigSchemaVersion).IsRequired().HasDefaultValue(1);

        builder
            .Property(e => e.MetadataJson)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");

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
            .HasIndex(e => new { e.BroadcasterId, e.EventType })
            .HasDatabaseName("IX_EventResponse_BroadcasterId_EventType");

        builder
            .HasIndex(e => e.PlatformSourceDefinitionId)
            .HasDatabaseName("IX_EventResponse_PlatformSourceDefinitionId");
    }
}
