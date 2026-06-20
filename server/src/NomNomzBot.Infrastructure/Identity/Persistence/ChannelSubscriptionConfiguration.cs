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
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Identity.Persistence;

public class ChannelSubscriptionConfiguration : IEntityTypeConfiguration<ChannelSubscription>
{
    public void Configure(EntityTypeBuilder<ChannelSubscription> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.BroadcasterId).IsRequired();

        builder.Property(e => e.Tier).IsRequired().HasMaxLength(20).HasDefaultValue("free");

        builder.Property(e => e.StripeCustomerId).HasMaxLength(255);

        builder.Property(e => e.StripeSubscriptionId).HasMaxLength(255);

        builder.Property(e => e.Status).IsRequired().HasMaxLength(20).HasDefaultValue("active");

        builder
            .HasIndex(e => e.BroadcasterId)
            .IsUnique()
            .HasDatabaseName("IX_ChannelSubscription_BroadcasterId");

        builder
            .HasOne(e => e.Channel)
            .WithOne()
            .HasForeignKey<ChannelSubscription>(e => e.BroadcasterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
