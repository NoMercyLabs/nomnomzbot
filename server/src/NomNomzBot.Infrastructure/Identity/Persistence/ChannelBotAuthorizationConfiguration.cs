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

public class ChannelBotAuthorizationConfiguration
    : IEntityTypeConfiguration<ChannelBotAuthorization>
{
    public void Configure(EntityTypeBuilder<ChannelBotAuthorization> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();

        builder.Property(e => e.IsActive).HasDefaultValue(true);

        builder
            .HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(e => e.BotAccount)
            .WithMany()
            .HasForeignKey(e => e.BotAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        // One authorization per (channel, bot account).
        builder
            .HasIndex(e => new { e.BroadcasterId, e.BotAccountId })
            .IsUnique()
            .HasDatabaseName("IX_ChannelBotAuthorization_Broadcaster_BotAccount");
    }
}
