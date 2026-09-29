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
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

/// <summary>
/// Maps the EventSub inbox (twitch-eventsub §10.1): unique Twitch message id (a resend is stored once) and
/// an index on the drain order.
/// </summary>
public class EventSubInboxMessageConfiguration : IEntityTypeConfiguration<EventSubInboxMessage>
{
    public void Configure(EntityTypeBuilder<EventSubInboxMessage> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.MessageId).IsRequired().HasMaxLength(255);
        builder.Property(e => e.SubscriptionType).IsRequired().HasMaxLength(100);
        builder.Property(e => e.SubscriptionVersion).IsRequired().HasMaxLength(20);
        builder.Property(e => e.TwitchBroadcasterUserId).IsRequired().HasMaxLength(255);
        builder.Property(e => e.EventJson).IsRequired();

        builder
            .HasIndex(e => e.MessageId)
            .IsUnique()
            .HasDatabaseName("UX_EventSubInboxMessage_MessageId");
        builder
            .HasIndex(e => new { e.ReceivedAt, e.Id })
            .HasDatabaseName("IX_EventSubInboxMessage_ReceivedAt_Id");
    }
}
