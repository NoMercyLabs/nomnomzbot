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
using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Infrastructure.Moderation.Persistence;

public class ViewerReportConfiguration : IEntityTypeConfiguration<ViewerReport>
{
    public void Configure(EntityTypeBuilder<ViewerReport> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.ReportedUserId).IsRequired();
        builder.Property(e => e.ReportedTwitchUserId).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Reason).IsRequired().HasMaxLength(500);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);

        // The mod queue reads open reports for a channel; the per-user panel reads reports about one viewer.
        builder.HasIndex(e => new { e.BroadcasterId, e.Status });
        builder.HasIndex(e => new { e.BroadcasterId, e.ReportedUserId });

        // Reference FKs are Restrict: a report is an audit row that outlives the channel/user delete (which is a
        // soft delete anyway), and Restrict keeps the migration free of multiple-cascade-path ambiguity.
        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(e => e.ReportedUser)
            .WithMany()
            .HasForeignKey(e => e.ReportedUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
