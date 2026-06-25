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

public class PipelineStepConditionConfiguration : IEntityTypeConfiguration<PipelineStepCondition>
{
    public void Configure(EntityTypeBuilder<PipelineStepCondition> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.PipelineStepId).IsRequired();
        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.ConditionType).IsRequired().HasMaxLength(40);
        builder.Property(e => e.Operator).HasMaxLength(20);
        builder.Property(e => e.LeftOperand).HasMaxLength(500);
        builder.Property(e => e.RightOperand).HasMaxLength(500);
        builder.Property(e => e.Negate).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.Order).IsRequired();

        builder
            .HasOne(e => e.Step)
            .WithMany(s => s.Conditions)
            .HasForeignKey(e => e.PipelineStepId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.PipelineStepId).HasDatabaseName("IX_PipelineStepCondition_StepId");
    }
}
