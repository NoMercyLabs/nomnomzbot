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

public class OutboundWebhookEndpointConfiguration
    : IEntityTypeConfiguration<OutboundWebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<OutboundWebhookEndpoint> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Fqdn).IsRequired().HasMaxLength(253);
        builder.Property(e => e.Path).HasMaxLength(255);
        builder.Property(e => e.SigningSecretEnvelope).IsRequired().HasMaxLength(1024);
        builder.Property(e => e.SecondarySigningSecretEnvelope).HasMaxLength(1024);
        builder.Property(e => e.DisabledReason).HasMaxLength(255);
        // Every outbound delivery is sent with a hardcoded Content-Type: application/json today (S-WEBHOOK-
        // JSON-FALLBACK), so JSON is the honest default for existing rows as well as new ones.
        builder.Property(e => e.BodyIsJson).HasDefaultValue(true);

        // SubscribedEventTypesJson / CustomHeadersJson are JSON text columns (the service owns (de)serialization).

        // Uniqueness of (channel, name) is service-enforced — soft-deletable, so a plain index.
        builder.HasIndex(e => new { e.BroadcasterId, e.Name });
    }
}
