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
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Widgets.Persistence;

public class RenderedAlertCaptureConfiguration : IEntityTypeConfiguration<RenderedAlertCapture>
{
    public void Configure(EntityTypeBuilder<RenderedAlertCapture> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.EventType).IsRequired().HasMaxLength(100);

        builder.Property(c => c.Payload).IsRequired().HasColumnType("jsonb");

        // Read pattern is "most recent N per broadcaster" (prune-on-write + a later replay lookup) — same
        // shape as IX_ChannelEvent_ChannelId_CreatedAt.
        builder
            .HasIndex(c => new { c.BroadcasterId, c.CreatedAt })
            .HasDatabaseName("IX_RenderedAlertCapture_BroadcasterId_CreatedAt");
    }
}
