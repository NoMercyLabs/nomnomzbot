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
using NomNomzBot.Infrastructure.Platform.Persistence.Converters;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

/// <summary>
/// Maps the per-tenant EventSub subscription registry (schema §F.7, twitch-eventsub §1). Guid PK, Guid tenant
/// key, the condition as a hand-rolled <c>[VC:JSON]</c> string column (no jsonb). Unique
/// <c>(BroadcasterId, Provider, EventType, Version)</c> guarantees one desired row per topic. The composing
/// tenant + soft-delete global filter is applied centrally (AppDbContext) — this configuration does not
/// re-declare a query filter. Deliberately no FK to Channels: platform-plane rows (the bot identity's own
/// topics, <c>BroadcasterId == Guid.Empty</c>) have no owning channel, and the soft-delete convention means
/// the cascade the FK carried could never fire anyway.
/// </summary>
public class EventSubSubscriptionConfiguration : IEntityTypeConfiguration<EventSubSubscription>
{
    public void Configure(EntityTypeBuilder<EventSubSubscription> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();

        builder.Property(e => e.Provider).IsRequired().HasMaxLength(20);
        builder.Property(e => e.EventType).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Version).IsRequired().HasMaxLength(20);

        builder
            .Property(e => e.Condition)
            .HasConversion(
                JsonValueConverter.Converter<Dictionary<string, string>>(),
                JsonValueConverter.Comparer<Dictionary<string, string>>()
            )
            .IsRequired();

        builder.Property(e => e.Transport).IsRequired().HasMaxLength(20);
        builder.Property(e => e.TwitchSubscriptionId).HasMaxLength(255);
        builder.Property(e => e.SessionId).HasMaxLength(255);
        builder.Property(e => e.ConduitId).HasMaxLength(255);
        builder.Property(e => e.ShardId).HasMaxLength(255);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Enabled).IsRequired();
        builder.Property(e => e.LastError).HasMaxLength(1000);

        builder
            .HasIndex(e => new
            {
                e.BroadcasterId,
                e.Provider,
                e.EventType,
                e.Version,
            })
            .IsUnique()
            .HasDatabaseName("UX_EventSubSubscription_Broadcaster_Provider_Type_Version");

        builder
            .HasIndex(e => e.TwitchSubscriptionId)
            .HasDatabaseName("IX_EventSubSubscription_TwitchSubscriptionId");
    }
}
