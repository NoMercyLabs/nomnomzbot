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
using NomNomzBot.Domain.Tts.Entities;

namespace NomNomzBot.Infrastructure.Tts.Persistence;

public class TtsApprovalQueueEntryConfiguration : IEntityTypeConfiguration<TtsApprovalQueueEntry>
{
    public void Configure(EntityTypeBuilder<TtsApprovalQueueEntry> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.RequestedByTwitchUserId).IsRequired().HasMaxLength(50);
        builder.Property(e => e.RequestedByDisplayName).HasMaxLength(255);
        builder.Property(e => e.OriginalText).IsRequired();
        builder.Property(e => e.VoiceId).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Provider).HasMaxLength(20);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);
        builder.Property(e => e.SourceMessageId).HasMaxLength(255);

        // The mod queue reads pending entries for a channel, newest-first (P.1a index).
        builder.HasIndex(e => new
        {
            e.BroadcasterId,
            e.Status,
            e.CreatedAt,
        });

        // Reference FK is Restrict: the entry is an audit row that outlives the channel delete (a soft delete
        // anyway), and Restrict keeps the migration free of multiple-cascade-path ambiguity. RequestedByUserId /
        // ReviewedByUserId are stored as bare guids (no nav): a pipeline/system utterance carries Guid.Empty, which
        // a hard Users FK would reject — the truthful key is the snapshotted Twitch id, as on TtsUsageRecord.
        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
