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
using NomNomzBot.Domain.Webhooks.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

public class OutboundWebhookDeliveryConfiguration
    : IEntityTypeConfiguration<OutboundWebhookDelivery>
{
    public void Configure(EntityTypeBuilder<OutboundWebhookDelivery> builder)
    {
        builder.HasKey(e => e.Id); // bigint identity

        builder.Property(e => e.EventType).IsRequired().HasMaxLength(150);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Error).HasMaxLength(1000);

        builder.HasIndex(e => new { e.EndpointId, e.CreatedAt });
        builder.HasIndex(e => new { e.Status, e.NextRetryAt }); // the retry-drain scan
    }
}
