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

        builder.Property(e => e.BroadcasterId).IsRequired().HasMaxLength(50);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(100);

        builder
            .Property(e => e.Permission)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("everyone");

        builder.Property(e => e.Type).IsRequired().HasMaxLength(20).HasDefaultValue("text");

        builder.Property(e => e.Response).HasMaxLength(2000);

        builder.Property(e => e.Responses).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");

        builder.Property(e => e.PipelineJson).HasColumnType("jsonb");

        builder.Property(e => e.IsEnabled).HasDefaultValue(true);

        builder.Property(e => e.Description).HasMaxLength(500);

        builder.Property(e => e.CooldownSeconds).IsRequired();

        builder.Property(e => e.CooldownPerUser).IsRequired();

        builder.Property(e => e.Aliases).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");

        builder.Property(e => e.IsPlatform).IsRequired();

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasIndex(e => new { e.Name, e.BroadcasterId })
            .IsUnique()
            .HasDatabaseName("IX_Command_Name_BroadcasterId");

        builder
            .HasIndex(e => new { e.BroadcasterId, e.IsEnabled })
            .HasDatabaseName("IX_Command_BroadcasterId_IsEnabled");

        builder.HasQueryFilter(e => e.DeletedAt == null);
    }
}
