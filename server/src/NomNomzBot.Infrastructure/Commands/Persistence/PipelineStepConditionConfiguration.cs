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
        builder.Property(e => e.ConditionType).IsRequired().HasMaxLength(40).HasDefaultValue("");
        builder.Property(e => e.GroupOp).HasMaxLength(3);
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

        // Condition-tree self-FK (pipeline-tree-and-editor.md §1.2, E2). Restrict, not cascade —
        // a parent-group delete goes through explicit subtree removal in the service layer, never
        // an implicit multi-path cascade off the same table.
        builder
            .HasOne<PipelineStepCondition>()
            .WithMany()
            .HasForeignKey(e => e.ParentConditionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.PipelineStepId).HasDatabaseName("IX_PipelineStepCondition_StepId");
        builder
            .HasIndex(e => e.ParentConditionId)
            .HasDatabaseName("IX_PipelineStepCondition_ParentConditionId");
    }
}
