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

/// <summary>One spam-defence policy per channel.</summary>
public class SpamDefensePolicyConfiguration : IEntityTypeConfiguration<SpamDefensePolicy>
{
    public void Configure(EntityTypeBuilder<SpamDefensePolicy> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.BroadcasterId).IsUnique();
    }
}

/// <summary>
/// The detection log (spam-defense.md §6.2).
///
/// <para>Indexed on (channel, time) descending because every read of this table is "show me what the
/// system has been doing lately" — the review queue, the dry-run report, and the detections page all
/// page backwards through one channel's recent verdicts.</para>
/// </summary>
public class SpamDetectionConfiguration : IEntityTypeConfiguration<SpamDetection>
{
    public void Configure(EntityTypeBuilder<SpamDetection> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.BroadcasterId, e.DetectedAt }).IsDescending(false, true);
        builder.HasIndex(e => new { e.BroadcasterId, e.SubjectPlatformUserId });

        builder.Property(e => e.SubjectPlatformUserId).HasMaxLength(100);
        builder.Property(e => e.SubjectDisplayName).HasMaxLength(100);
        builder.Property(e => e.Provider).HasMaxLength(50);
        builder.Property(e => e.MessageId).HasMaxLength(100);
        builder.Property(e => e.MessageText).HasMaxLength(1000);
        builder.Property(e => e.Skeleton).HasMaxLength(1000);
        builder.Property(e => e.Signals).HasMaxLength(500);
        builder.Property(e => e.Reason).HasMaxLength(1000);
    }
}

/// <summary>Correlated cohorts, read newest-first per channel like every other moderation log.</summary>
public class SpamCampaignRecordConfiguration : IEntityTypeConfiguration<SpamCampaignRecord>
{
    public void Configure(EntityTypeBuilder<SpamCampaignRecord> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.BroadcasterId, e.LastSeenAt }).IsDescending(false, true);
        builder.HasIndex(e => new { e.BroadcasterId, e.Skeleton });

        builder.Property(e => e.Skeleton).HasMaxLength(1000);
        builder.Property(e => e.ActionedAccountIds).HasMaxLength(4000);
        builder.Property(e => e.MemberAccountIds).HasMaxLength(8000);
        builder.Property(e => e.StandingAccountIds).HasMaxLength(4000);
        builder.Property(e => e.ReversalReason).HasMaxLength(500);
    }
}

/// <summary>
/// Follow-bot blocks. Indexed by batch as well as by channel, because the operation that matters most
/// here is restoring a whole sweep at once when a viral moment was misread.
/// </summary>
public class FollowBotBlockConfiguration : IEntityTypeConfiguration<FollowBotBlock>
{
    public void Configure(EntityTypeBuilder<FollowBotBlock> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.BroadcasterId, e.BlockedAt }).IsDescending(false, true);
        builder.HasIndex(e => e.BatchId);

        builder.Property(e => e.SubjectPlatformUserId).HasMaxLength(100);
        builder.Property(e => e.SubjectUsername).HasMaxLength(100);
        builder.Property(e => e.Indicators).HasMaxLength(500);
    }
}
