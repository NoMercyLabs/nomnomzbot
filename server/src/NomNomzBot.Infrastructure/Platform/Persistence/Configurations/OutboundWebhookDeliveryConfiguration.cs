// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
