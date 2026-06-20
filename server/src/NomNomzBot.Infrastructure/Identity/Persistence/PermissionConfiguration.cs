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
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Identity.Persistence;

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();

        builder.Property(e => e.SubjectType).IsRequired().HasMaxLength(10);

        builder.Property(e => e.SubjectId).IsRequired().HasMaxLength(50);

        builder.Property(e => e.ResourceType).IsRequired().HasMaxLength(20);

        builder.Property(e => e.ResourceId).HasMaxLength(255);

        builder.Property(e => e.PermissionValue).IsRequired().HasMaxLength(5);

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasIndex(e => new
            {
                e.BroadcasterId,
                e.SubjectType,
                e.SubjectId,
                e.ResourceType,
            })
            .IsUnique()
            .HasDatabaseName("IX_Permission_BroadcasterId_Subject_ResourceType");
    }
}
