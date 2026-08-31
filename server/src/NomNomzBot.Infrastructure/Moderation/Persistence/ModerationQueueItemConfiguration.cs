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

public class ModerationQueueItemConfiguration : IEntityTypeConfiguration<ModerationQueueItem>
{
    public void Configure(EntityTypeBuilder<ModerationQueueItem> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.TargetTwitchUserId).HasMaxLength(50);
        builder.Property(e => e.TargetUsernameSnapshot).HasMaxLength(50);
        builder.Property(e => e.AutoModMessageId).HasMaxLength(100);
        builder.Property(e => e.MessageContentSnapshot).HasMaxLength(500);
        builder.Property(e => e.AutoModCategory).HasMaxLength(50);
        builder.Property(e => e.ResolutionAction).HasMaxLength(20);

        // The queue panel lists pending items for a channel; the update-event handler looks a held row up by
        // its Twitch message id to resolve it when Twitch reports the verdict.
        builder.HasIndex(e => new { e.BroadcasterId, e.Status });
        builder.HasIndex(e => new { e.BroadcasterId, e.AutoModMessageId });
    }
}
