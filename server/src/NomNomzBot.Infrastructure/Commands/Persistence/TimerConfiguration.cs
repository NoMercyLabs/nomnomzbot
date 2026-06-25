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
        builder.Property(e => e.ConfigSchemaVersion).IsRequired().HasDefaultValue(1);

        builder.Property(e => e.Messages).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne<Domain.Commands.Entities.Pipeline>()
            .WithMany()
            .HasForeignKey(e => e.PipelineId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder
            .HasIndex(e => new { e.BroadcasterId, e.IsEnabled })
            .HasDatabaseName("IX_Timer_BroadcasterId_IsEnabled");
    }
}
