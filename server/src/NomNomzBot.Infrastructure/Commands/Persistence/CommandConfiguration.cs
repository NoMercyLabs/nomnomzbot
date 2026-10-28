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
using NomNomzBot.Domain.CustomCode.Entities;

namespace NomNomzBot.Infrastructure.Commands.Persistence;

public class CommandConfiguration : IEntityTypeConfiguration<Command>
{
    public void Configure(EntityTypeBuilder<Command> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();

        builder.Property(e => e.Name).IsRequired().HasMaxLength(100);

        builder.Property(e => e.NameNormalized).IsRequired().HasMaxLength(100);

        builder.Property(e => e.Tier).IsRequired().HasMaxLength(20).HasDefaultValue("template");

        builder.Property(e => e.MinPermissionLevel).IsRequired().HasDefaultValue(0);

        builder.Property(e => e.TemplateResponse).HasMaxLength(2000);

        builder
            .Property(e => e.TemplateResponses)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb");

        builder.Property(e => e.IsEnabled).HasDefaultValue(true);

        builder.Property(e => e.SpeakWithTts).HasDefaultValue(false);

        builder.Property(e => e.ReplyDelivery).HasReplyDeliveryColumn();

        builder.Property(e => e.Description).HasMaxLength(500);

        builder.Property(e => e.CooldownSeconds).IsRequired();

        builder.Property(e => e.Aliases).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");

        builder.Property(e => e.IsPlatform).IsRequired();

        builder.Property(e => e.UseCount).IsRequired().HasDefaultValue(0L);

        builder.Property(e => e.PresetKey).HasMaxLength(50);

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
            .HasOne<CodeScript>()
            .WithMany()
            .HasForeignKey(e => e.CodeScriptId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasIndex(e => e.CodeScriptId).HasDatabaseName("IX_Command_CodeScriptId");

        builder.HasIndex(e => e.PipelineId).HasDatabaseName("IX_Command_PipelineId");

        // Unique among LIVE commands only. A deleted command stays behind as a soft-deleted tombstone (the
        // audit trail names it), so an index over every row made the name unusable forever: re-creating a
        // deleted "!8ball" failed with a constraint error, and so did every seeder or import that wrote it.
        builder
            .HasIndex(e => new { e.NameNormalized, e.BroadcasterId })
            .IsUnique()
            .HasDatabaseName("IX_Command_NameNormalized_BroadcasterId")
            .HasFilter("\"DeletedAt\" IS NULL");

        builder
            .HasIndex(e => new { e.BroadcasterId, e.IsEnabled })
            .HasDatabaseName("IX_Command_BroadcasterId_IsEnabled");
    }
}
