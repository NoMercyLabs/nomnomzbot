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
using NomNomzBot.Domain.Billing.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.StripeCustomerIdCipher).HasMaxLength(512);
        builder.Property(e => e.StripeSubscriptionId).HasMaxLength(255);
        builder.Property(e => e.BillingEmailCipher).HasMaxLength(512);

        // One subscription per channel — enforced in the service (soft-deletable, so a plain index).
        builder.HasIndex(e => e.BroadcasterId);
        builder.HasIndex(e => e.StripeSubscriptionId);
    }
}
