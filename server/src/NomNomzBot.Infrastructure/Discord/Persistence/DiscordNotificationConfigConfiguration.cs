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
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Newtonsoft.Json;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Discord.ValueObjects;

namespace NomNomzBot.Infrastructure.Discord.Persistence;

public class DiscordNotificationConfigConfiguration
    : IEntityTypeConfiguration<DiscordNotificationConfig>
{
    public void Configure(EntityTypeBuilder<DiscordNotificationConfig> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();
        builder.Property(e => e.GuildConnectionId).IsRequired();
        builder.Property(e => e.TriggerType).IsRequired().HasMaxLength(30);
        builder.Property(e => e.TargetChannelId).IsRequired().HasMaxLength(50);
        builder.Property(e => e.MilestoneType).HasMaxLength(20);
        builder.Property(e => e.ConfigSchemaVersion).IsRequired().HasDefaultValue(1);

        // [VC:JSON] — the EmbedConfig value object is serialized to a single text column via Newtonsoft
        // (project rule: Newtonsoft for app-JSON value converters; no jsonb column type).
        ValueConverter<DiscordEmbedConfig?, string?> embedConverter = new(
            v => v == null ? null : JsonConvert.SerializeObject(v),
            v =>
                string.IsNullOrEmpty(v)
                    ? null
                    : JsonConvert.DeserializeObject<DiscordEmbedConfig>(v)
        );
        ValueComparer<DiscordEmbedConfig?> embedComparer = new(
            (a, b) => (a == null && b == null) || (a != null && b != null && a.Equals(b)),
            v => v == null ? 0 : v.GetHashCode(),
            v => v
        );
        builder
            .Property(e => e.EmbedConfig)
            .HasConversion(embedConverter)
            .Metadata.SetValueComparer(embedComparer);

        builder.Property(e => e.TriggerType).HasMaxLength(30);
        builder.HasIndex(e => e.GuildConnectionId);
        builder.HasIndex(e => e.TriggerType);
        builder.HasIndex(e => e.PingRoleId);

        // One rule per (guild connection, trigger).
        builder.HasIndex(e => new { e.GuildConnectionId, e.TriggerType }).IsUnique();

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(e => e.GuildConnection)
            .WithMany()
            .HasForeignKey(e => e.GuildConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Single nullable ping role; nulled (not cascade-deleted) when the role is removed — handled in the
        // role service's delete path. Restrict so the DB never cascades a config away with its ping role.
        builder
            .HasOne(e => e.PingRole)
            .WithMany()
            .HasForeignKey(e => e.PingRoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
