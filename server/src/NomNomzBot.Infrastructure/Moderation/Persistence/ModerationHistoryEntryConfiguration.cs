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

public class ModerationHistoryEntryConfiguration : IEntityTypeConfiguration<ModerationHistoryEntry>
{
    public void Configure(EntityTypeBuilder<ModerationHistoryEntry> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();

        builder.Property(e => e.SubjectUserId).IsRequired();

        builder.Property(e => e.SubjectTwitchUserId).IsRequired().HasMaxLength(50);

        builder.Property(e => e.ActionType).IsRequired().HasMaxLength(20);

        builder.Property(e => e.ModeratorTwitchUserId).HasMaxLength(50);

        builder.Property(e => e.ModeratorDisplayName).HasMaxLength(100);

        builder.Property(e => e.Reason).HasMaxLength(500);

        builder.Property(e => e.OccurredAt).IsRequired();

        // The channel-wide browsable log (owner punch list §3/§12): newest first, optionally narrowed to one
        // subject or one date range — both covered by leading BroadcasterId + OccurredAt, with SubjectUserId
        // as a second index for the per-person filter (Community Profile + future Moderation History page).
        builder
            .HasIndex(e => new { e.BroadcasterId, e.OccurredAt })
            .HasDatabaseName("IX_ModerationHistoryEntry_Broadcaster_OccurredAt");

        builder
            .HasIndex(e => new
            {
                e.BroadcasterId,
                e.SubjectUserId,
                e.OccurredAt,
            })
            .HasDatabaseName("IX_ModerationHistoryEntry_Broadcaster_Subject_OccurredAt");
    }
}
