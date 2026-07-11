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
using NomNomzBot.Domain.Chat.Entities;

namespace NomNomzBot.Infrastructure.Chat.Persistence;

/// <summary>
/// Maps the YouTube ban-id ledger. The lookup an unban performs is "newest live row for this viewer in this
/// channel", so the index composes <c>(BroadcasterId, BannedChannelId)</c>; rows are soft-deleted on consume
/// (the composing tenant + soft-delete global filter applies centrally). No FK to Channels — the pattern set
/// by EventSubSubscriptions: soft-delete-only means the cascade could never fire.
/// </summary>
public class YouTubeLiveChatBanConfiguration : IEntityTypeConfiguration<YouTubeLiveChatBan>
{
    public void Configure(EntityTypeBuilder<YouTubeLiveChatBan> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.LiveChatId).IsRequired().HasMaxLength(255);
        builder.Property(e => e.BannedChannelId).IsRequired().HasMaxLength(100);
        builder.Property(e => e.BanId).IsRequired().HasMaxLength(255);
        builder.Property(e => e.BanType).IsRequired().HasMaxLength(20);

        builder
            .HasIndex(e => new { e.BroadcasterId, e.BannedChannelId })
            .HasDatabaseName("IX_YouTubeLiveChatBan_Broadcaster_BannedChannel");
    }
}
