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
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Identity.Persistence;

/// <summary>
/// Maps <see cref="ChannelMissingScope"/> (identity-auth §3.4a). The <c>(BroadcasterId, Scope)</c> unique index
/// is the idempotency guarantee: at most one row per channel per scope, so a re-detection is an upsert (never a
/// duplicate) and the chat notice fires exactly once per missing-scope-set. Cascade-deletes with the channel.
/// </summary>
public sealed class ChannelMissingScopeConfiguration : IEntityTypeConfiguration<ChannelMissingScope>
{
    public void Configure(EntityTypeBuilder<ChannelMissingScope> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();

        builder.Property(e => e.Scope).IsRequired().HasMaxLength(100);

        builder.Property(e => e.Feature).HasMaxLength(50);

        builder.Property(e => e.DetectedAt).IsRequired();

        // One row per (channel, scope): the reactive-detection upsert anchor + chat-notice idempotency key.
        builder.HasIndex(e => new { e.BroadcasterId, e.Scope }).IsUnique();

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
