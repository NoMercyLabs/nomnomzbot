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
using NomNomzBot.Domain.Commands.Entities;

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

        builder.Property(e => e.Description).HasMaxLength(500);

        builder.Property(e => e.CooldownSeconds).IsRequired();

        builder.Property(e => e.CooldownPerUser).IsRequired();

        builder.Property(e => e.Aliases).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");

        builder.Property(e => e.IsPlatform).IsRequired();

        builder.Property(e => e.UseCount).IsRequired().HasDefaultValue(0L);

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

        builder.HasIndex(e => e.PipelineId).HasDatabaseName("IX_Command_PipelineId");

        builder
            .HasIndex(e => new { e.NameNormalized, e.BroadcasterId })
            .IsUnique()
            .HasDatabaseName("IX_Command_NameNormalized_BroadcasterId");

        builder
            .HasIndex(e => new { e.BroadcasterId, e.IsEnabled })
            .HasDatabaseName("IX_Command_BroadcasterId_IsEnabled");
    }
}
